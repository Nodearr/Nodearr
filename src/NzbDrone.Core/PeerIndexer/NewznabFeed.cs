using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.PeerIndexer
{
    // The Newznab documents Nodearr answers the Arr apps with. See ADR 0010 for what Sonarr,
    // Radarr and Prowlarr need from each, and ADR 0015 for what goes in them.
    public static class NewznabFeed
    {
        public const int TvCategory = 5000;
        public const int MovieCategory = 2000;
        public const string CandidateMetaType = "x-nodearr-candidate";
        public const string AirDateMetaType = "x-nodearr-airdate";
        public const string ConnectionTestTitle = "Nodearr connection test, not a release";

        private const string GuidPrefix = "nodearr:";

        private static readonly XNamespace Newznab = "http://www.newznab.com/DTD/2010/feeds/attributes/";
        private static readonly XNamespace Nzb = "http://www.newzbin.com/DTD/2003/nzb";
        private static readonly DateTime FallbackEpoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static XElement Caps(string baseUrl)
        {
            // The TV and movie searches take IDs and no text, so the Arr apps never fall back to
            // guessing by title there. Plain search takes text, for people searching by hand.
            return new XElement("caps",
                new XElement("server", new XAttribute("version", "1.0"), new XAttribute("title", "Nodearr"), new XAttribute("url", baseUrl)),
                new XElement("limits", new XAttribute("max", CandidateService.MaxLimit), new XAttribute("default", CandidateService.MaxLimit)),
                new XElement("registration", new XAttribute("available", "no"), new XAttribute("open", "no")),
                new XElement("searching",
                    new XElement("search", new XAttribute("available", "yes"), new XAttribute("supportedParams", "q")),
                    new XElement("tv-search", new XAttribute("available", "yes"), new XAttribute("supportedParams", "tvdbid,imdbid,tmdbid,season,ep")),
                    new XElement("movie-search", new XAttribute("available", "yes"), new XAttribute("supportedParams", "imdbid,tmdbid"))),
                new XElement("categories",
                    Category(MovieCategory, "Movies", (2030, "SD"), (2040, "HD"), (2045, "UHD")),
                    Category(TvCategory, "TV", (5030, "SD"), (5040, "HD"), (5045, "UHD"), (5070, "Anime"))));
        }

        public static XElement Rss(string baseUrl, int offset, int total, IEnumerable<XElement> items)
        {
            var channel = new XElement("channel",
                new XElement("title", "Nodearr"),
                new XElement("description", "What trusted nodes share with this one"),
                new XElement("link", baseUrl),
                new XElement(Newznab + "response", new XAttribute("offset", offset), new XAttribute("total", total)));

            channel.Add(items);

            return new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "newznab", Newznab),
                channel);
        }

        public static XElement Error(int code, string description)
        {
            return new XElement("error", new XAttribute("code", code), new XAttribute("description", description));
        }

        public static XElement Item(PeerCatalogItem item, string link)
        {
            return Item(item, link, null);
        }

        // airDate is set for an item that was found by the day its episode aired.
        public static XElement Item(PeerCatalogItem item, string link, string airDate)
        {
            var isMovie = item.MediaKind == MediaKind.Movie;
            var family = isMovie ? MovieCategory : TvCategory;
            var published = PublishedAt(item).ToString("r");
            var title = TitleFor(item, airDate);

            var element = new XElement("item",
                new XElement("title", title),
                new XElement("guid", new XAttribute("isPermaLink", "false"), GuidPrefix + item.CandidateId),
                new XElement("link", link),
                new XElement("pubDate", published),
                new XElement("category", isMovie ? "Movies" : "TV"),
                new XElement("description", title),
                new XElement("enclosure", new XAttribute("url", link), new XAttribute("length", item.Size), new XAttribute("type", "application/x-nzb")),
                Attr("category", family));

            if (item.Resolution > 0)
            {
                element.Add(Attr("category", family + (item.Resolution >= 2160 ? 45 : item.Resolution >= 720 ? 40 : 30)));
            }

            element.Add(Attr("size", item.Size), Attr("usenetdate", published));

            if (isMovie)
            {
                AddId(element, "tmdbid", item.TmdbId);
            }
            else
            {
                AddId(element, "tvdbid", item.TvdbId);
                element.Add(Attr("season", "S" + item.SeasonNumber.ToString("00")));

                var episodes = item.GetEpisodeNumbers();

                if (episodes.Count > 0)
                {
                    element.Add(Attr("episode", "E" + episodes[0].ToString("00")));
                }
            }

            var imdbDigits = new string((item.ImdbId ?? string.Empty).Where(char.IsDigit).ToArray());

            if (imdbDigits.Length > 0)
            {
                element.Add(Attr("imdb", imdbDigits));
            }

            return element;
        }

        // The Arr apps refuse to save an indexer whose test query comes back empty (ADR 0010,
        // finding 1). One stand-in per kind asked for gets a new node past that. Its title matches
        // no series or movie, and its link leads nowhere.
        public static IEnumerable<XElement> ConnectionTestItems(bool tv, bool movies, IReadOnlyCollection<int> requestedCategories, string baseUrl)
        {
            foreach (var family in new[] { TvCategory, MovieCategory })
            {
                if (family == TvCategory ? !tv : !movies)
                {
                    continue;
                }

                var asked = requestedCategories.Where(c => c / 1000 * 1000 == family).ToList();
                var kind = family == TvCategory ? "tv" : "movie";
                var published = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("r");
                var link = $"{baseUrl}/newznab/api?t=get&id=connection-test";

                yield return new XElement("item",
                    new XElement("title", ConnectionTestTitle),
                    new XElement("guid", new XAttribute("isPermaLink", "false"), $"{GuidPrefix}connection-test:{kind}"),
                    new XElement("link", link),
                    new XElement("pubDate", published),
                    new XElement("category", family == TvCategory ? "TV" : "Movies"),
                    new XElement("description", ConnectionTestTitle),
                    new XElement("enclosure", new XAttribute("url", link), new XAttribute("length", 1), new XAttribute("type", "application/x-nzb")),
                    Attr("category", family),
                    Attr("category", asked.Count > 0 ? asked[0] : family + 40),
                    Attr("size", 1),
                    Attr("usenetdate", published));
            }
        }

        // The NZB is only a ticket. It names the candidate, and Nodearr's download client does the
        // rest. The Arr apps need an <nzb> root with at least one <file> in it.
        public static XElement Ticket(PeerCatalogItem item)
        {
            return Ticket(item, null);
        }

        // airDate is set for a ticket to an episode that was found by the day it aired. The
        // download is then named with that day too, so Sonarr sees one title from search to import.
        public static XElement Ticket(PeerCatalogItem item, string airDate)
        {
            var title = TitleFor(item, airDate);

            var head = new XElement(Nzb + "head",
                new XElement(Nzb + "meta", new XAttribute("type", "title"), title),
                new XElement(Nzb + "meta", new XAttribute("type", CandidateMetaType), item.CandidateId));

            if (airDate != null)
            {
                head.Add(new XElement(Nzb + "meta", new XAttribute("type", AirDateMetaType), airDate));
            }

            return new XElement(Nzb + "nzb",
                head,
                new XElement(Nzb + "file",
                    new XAttribute("poster", "nodearr"),
                    new XAttribute("date", new DateTimeOffset(PublishedAt(item)).ToUnixTimeSeconds()),
                    new XAttribute("subject", title),
                    new XElement(Nzb + "groups", new XElement(Nzb + "group", "nodearr.transfer")),
                    new XElement(Nzb + "segments",
                        new XElement(Nzb + "segment", new XAttribute("bytes", item.Size), new XAttribute("number", 1), item.CandidateId + "@nodearr"))));
        }

        // Reads the candidate a ticket names, or null when the document is not one.
        public static string ReadTicket(XDocument nzb)
        {
            return ReadMeta(nzb, CandidateMetaType);
        }

        // The day a ticket was found by, or null when it was found by number.
        public static string ReadTicketAirDate(XDocument nzb)
        {
            return ReadMeta(nzb, AirDateMetaType);
        }

        private static string ReadMeta(XDocument nzb, string type)
        {
            return nzb.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "meta" && (string)e.Attribute("type") == type)?.Value;
        }

        // The title for an item: the one kept with it, or one written with the day it was found by.
        public static string TitleFor(PeerCatalogItem item, string airDate)
        {
            return airDate != null && item.GetAirDates().Contains(airDate) ? ReleaseTitleBuilder.For(item, airDate) : item.ReleaseTitle;
        }

        // The Arr apps tell a release they have blocklisted by its title and this date (ADR 0010,
        // finding 4), so it must never change for a file. It is when the peer's Arr app imported
        // the file, which also keeps two peers' copies of one release apart.
        public static DateTime PublishedAt(PeerCatalogItem item)
        {
            if (item.DateAdded.HasValue)
            {
                return item.DateAdded.Value.ToUniversalTime();
            }

            // No date from the peer: one worked out from the candidate ID, so it is still the same every time.
            var seed = (item.CandidateId ?? string.Empty).Aggregate(0u, (hash, c) => unchecked((hash * 31) + c));

            return FallbackEpoch.AddSeconds(seed % (365 * 24 * 3600));
        }

        private static void AddId(XElement element, string name, int value)
        {
            if (value > 0)
            {
                element.Add(Attr(name, value));
            }
        }

        private static XElement Category(int id, string name, params (int Id, string Name)[] subcategories)
        {
            return new XElement("category",
                new XAttribute("id", id),
                new XAttribute("name", name),
                subcategories.Select(s => new XElement("subcat", new XAttribute("id", s.Id), new XAttribute("name", s.Name))));
        }

        private static XElement Attr(string name, object value)
        {
            return new XElement(Newznab + "attr", new XAttribute("name", name), new XAttribute("value", value));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;

namespace Nodearr.Http.ArrContractSpike
{
    // S1 spike: the smallest Newznab surface Sonarr, Radarr and Prowlarr accept.
    // Anonymous at the framework level so a bad key gets a Newznab error document instead of a bare 401.
    [AllowAnonymous]
    public class NewznabSpikeController : Controller
    {
        private static readonly XNamespace Newznab = "http://www.newznab.com/DTD/2010/feeds/attributes/";
        private static readonly XNamespace NzbNamespace = "http://www.newzbin.com/DTD/2003/nzb";

        private readonly IArrContractSpikeStore _store;
        private readonly IConfigFileProvider _configFileProvider;

        public NewznabSpikeController(IArrContractSpikeStore store, IConfigFileProvider configFileProvider)
        {
            _store = store;
            _configFileProvider = configFileProvider;
        }

        [HttpGet("/newznab/api")]
        public IActionResult Api([FromQuery] string t, [FromQuery] string apikey)
        {
            if (string.Equals(t, "caps", StringComparison.OrdinalIgnoreCase))
            {
                return Xml(Caps(), "caps");
            }

            if (apikey != _configFileProvider.ApiKey)
            {
                return Error(100, "Incorrect user credentials");
            }

            switch (t?.ToLowerInvariant())
            {
                case "search":
                case "tvsearch":
                case "movie":
                    return Search(t.ToLowerInvariant());
                case "get":
                    return Get();
                default:
                    return Error(202, "No such function");
            }
        }

        private IActionResult Search(string function)
        {
            var query = Request.Query;
            var candidates = _store.GetConfig().Candidates.AsEnumerable();

            if (function == "tvsearch")
            {
                candidates = candidates.Where(c => !c.IsMovie);
            }
            else if (function == "movie")
            {
                candidates = candidates.Where(c => c.IsMovie);
            }

            if (query.TryGetValue("cat", out var cat) && !string.IsNullOrWhiteSpace(cat))
            {
                var requested = cat.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

                candidates = candidates.Where(c => requested.Any(r => r == c.Category || r == c.Category / 1000 * 1000));
            }

            // Arr sends every ID it knows in one request (Sonarr: tvdbid and imdbid together), and not every
            // candidate carries every ID. A candidate matches when any ID it does carry agrees.
            var hasTvdbId = int.TryParse(query["tvdbid"], out var tvdbId);
            var hasTmdbId = int.TryParse(query["tmdbid"], out var tmdbId);
            var imdbId = query["imdbid"].ToString().TrimStart('t');

            if (hasTvdbId || hasTmdbId || imdbId.Length > 0)
            {
                candidates = candidates.Where(c =>
                    (hasTvdbId && c.TvdbId == tvdbId) ||
                    (hasTmdbId && c.TmdbId == tmdbId) ||
                    (imdbId.Length > 0 && c.ImdbId != null && c.ImdbId.TrimStart('t') == imdbId));
            }

            if (int.TryParse(query["season"], out var season))
            {
                candidates = candidates.Where(c => c.Season == season);
            }

            if (int.TryParse(query["ep"], out var episode))
            {
                candidates = candidates.Where(c => c.Episode == episode);
            }

            if (query.TryGetValue("q", out var q) && !string.IsNullOrWhiteSpace(q))
            {
                var words = Normalize(q).Split(' ', StringSplitOptions.RemoveEmptyEntries);

                candidates = candidates.Where(c => words.All(w => Normalize(c.Title).Contains(w)));
            }

            var results = candidates.ToList();
            var offset = int.TryParse(query["offset"], out var o) ? o : 0;
            var limit = int.TryParse(query["limit"], out var l) ? l : 100;

            var channel = new XElement("channel",
                new XElement("title", "Nodearr"),
                new XElement("description", "Nodearr trusted peer availability"),
                new XElement("link", BaseUrl()),
                new XElement(Newznab + "response", new XAttribute("offset", offset), new XAttribute("total", results.Count)));

            foreach (var candidate in results.Skip(offset).Take(limit))
            {
                channel.Add(Item(candidate));
            }

            var rss = new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "newznab", Newznab),
                channel);

            return Xml(rss, $"{results.Count} result(s): {string.Join(", ", results.Select(r => r.Id))}");
        }

        private XElement Item(SpikeCandidate candidate)
        {
            var link = $"{BaseUrl()}/newznab/api?t=get&id={Uri.EscapeDataString(candidate.Id)}&apikey={_configFileProvider.ApiKey}";
            var size = candidate.GetSize();
            var published = candidate.PublishedAt.ToUniversalTime().ToString("r");

            var item = new XElement("item",
                new XElement("title", candidate.Title),
                new XElement("guid", new XAttribute("isPermaLink", "false"), "nodearr:" + candidate.Id),
                new XElement("link", link),
                new XElement("pubDate", published),
                new XElement("category", candidate.IsMovie ? "Movies" : "TV"),
                new XElement("description", candidate.Title),
                new XElement("enclosure", new XAttribute("url", link), new XAttribute("length", size), new XAttribute("type", "application/x-nzb")),
                Attr("category", candidate.Category / 1000 * 1000),
                Attr("category", candidate.Category),
                Attr("size", size),
                Attr("usenetdate", published));

            if (candidate.IsMovie)
            {
                item.Add(Attr("tmdbid", candidate.TmdbId));
            }
            else
            {
                item.Add(Attr("tvdbid", candidate.TvdbId));
                item.Add(Attr("season", "S" + candidate.Season.ToString("00")));
                item.Add(Attr("episode", "E" + candidate.Episode.ToString("00")));
            }

            if (!string.IsNullOrWhiteSpace(candidate.ImdbId))
            {
                item.Add(Attr("imdb", candidate.ImdbId.TrimStart('t')));
            }

            return item;
        }

        private IActionResult Get()
        {
            var id = Request.Query["id"].ToString();
            var candidate = _store.GetConfig().Candidates.SingleOrDefault(c => c.Id == id);

            if (candidate == null)
            {
                return Error(300, "No such item");
            }

            // The NZB is only a ticket. It names the candidate, and the download client surface does the rest.
            // Arr requires an <nzb> root with at least one <file>.
            var nzb = new XElement(NzbNamespace + "nzb",
                new XElement(NzbNamespace + "head",
                    new XElement(NzbNamespace + "meta", new XAttribute("type", "title"), candidate.Title),
                    new XElement(NzbNamespace + "meta", new XAttribute("type", "x-nodearr-candidate"), candidate.Id)),
                new XElement(NzbNamespace + "file",
                    new XAttribute("poster", "nodearr"),
                    new XAttribute("date", new DateTimeOffset(candidate.PublishedAt.ToUniversalTime()).ToUnixTimeSeconds()),
                    new XAttribute("subject", candidate.Title),
                    new XElement(NzbNamespace + "groups", new XElement(NzbNamespace + "group", "nodearr.transfer")),
                    new XElement(NzbNamespace + "segments",
                        new XElement(NzbNamespace + "segment", new XAttribute("bytes", candidate.GetSize()), new XAttribute("number", 1), candidate.Id + "@nodearr"))));

            var bytes = Encoding.UTF8.GetBytes(new XDeclaration("1.0", "utf-8", null) + Environment.NewLine + nzb);

            _store.Record(Request, 200, $"NZB ticket for {candidate.Id}, {bytes.Length} bytes");

            return File(bytes, "application/x-nzb", candidate.Title + ".nzb");
        }

        private XElement Caps()
        {
            return new XElement("caps",
                new XElement("server", new XAttribute("version", "1.0"), new XAttribute("title", "Nodearr"), new XAttribute("url", BaseUrl())),
                new XElement("limits", new XAttribute("max", 100), new XAttribute("default", 100)),
                new XElement("registration", new XAttribute("available", "no"), new XAttribute("open", "no")),
                new XElement("searching",
                    new XElement("search", new XAttribute("available", "yes"), new XAttribute("supportedParams", "q")),
                    new XElement("tv-search", new XAttribute("available", "yes"), new XAttribute("supportedParams", "q,tvdbid,imdbid,season,ep")),
                    new XElement("movie-search", new XAttribute("available", "yes"), new XAttribute("supportedParams", "q,imdbid,tmdbid"))),
                new XElement("categories",
                    Category(2000, "Movies", new Dictionary<int, string> { { 2030, "SD" }, { 2040, "HD" }, { 2045, "UHD" } }),
                    Category(5000, "TV", new Dictionary<int, string> { { 5030, "SD" }, { 5040, "HD" }, { 5045, "UHD" } })));
        }

        private static XElement Category(int id, string name, Dictionary<int, string> subcategories)
        {
            return new XElement("category",
                new XAttribute("id", id),
                new XAttribute("name", name),
                subcategories.Select(s => new XElement("subcat", new XAttribute("id", s.Key), new XAttribute("name", s.Value))));
        }

        private static XElement Attr(string name, object value)
        {
            return new XElement(Newznab + "attr", new XAttribute("name", name), new XAttribute("value", value));
        }

        private static string Normalize(string value)
        {
            return new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        }

        private string BaseUrl()
        {
            return $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        }

        private IActionResult Error(int code, string description)
        {
            return Xml(new XElement("error", new XAttribute("code", code), new XAttribute("description", description)), $"error {code}: {description}");
        }

        private IActionResult Xml(XElement root, string summary)
        {
            _store.Record(Request, 200, summary);

            return Content(new XDeclaration("1.0", "utf-8", null) + Environment.NewLine + root, "application/xml", Encoding.UTF8);
        }
    }
}

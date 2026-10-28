using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.PeerIndexer
{
    // What an Arr app asked the indexer for. Zero, null and empty mean "not given".
    public class CandidateQuery
    {
        public bool IncludeTv { get; set; } = true;
        public bool IncludeMovies { get; set; } = true;

        // Adult content, from peers' Whisparr. Found only when asked for by its own categories, and
        // never by a search for movies or TV.
        public bool IncludeAdult { get; set; }

        // Albums, from peers' Lidarr. Found only when asked for by their own categories, and by
        // text: the artist and album Lidarr sends, and any other words.
        public bool IncludeMusic { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public int TvdbId { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }

        // An ID kind Nodearr does not hold, such as a TVRage ID. Such a query matches nothing,
        // which is not the same as a query with no IDs at all.
        public bool HasUnknownIds { get; set; }
        public int? Season { get; set; }
        public string Episode { get; set; }
        public string Text { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; } = CandidateService.MaxLimit;
        public int? RssApplicationId { get; set; }
        public bool IncludeUpgrades { get; set; } = true;

        // Sonarr asks for an episode of a daily show by the day it aired: the year as the season
        // and "MM/dd" as the episode. This is that day as yyyy-MM-dd, or null for any other query.
        public string AirDate =>
            Season.HasValue && DateTime.TryParseExact($"{Season.Value:0000}/{Episode}", "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null;

        public bool HasIds => TvdbId > 0 || TmdbId > 0 || ImdbId.IsNotNullOrWhiteSpace() || HasUnknownIds;

        // Everything Lidarr said to look for, as words.
        public string MusicText => string.Join(" ", new[] { Artist, Album, Text }.Where(t => t.IsNotNullOrWhiteSpace()));

        // No IDs and no text: the Arr app's connection test, or its RSS sync.
        public bool IsBrowse => !HasIds && Text.IsNullOrWhiteSpace() && Artist.IsNullOrWhiteSpace() && Album.IsNullOrWhiteSpace();
    }

    public class CandidateResult
    {
        public int Total { get; set; }
        public List<PeerCatalogItem> Items { get; set; } = new();

        // For the items that were found by the day an episode aired: the candidate and that day.
        // Their titles have to be written with the day. See ReleaseTitleBuilder.
        public Dictionary<string, string> AirDates { get; set; } = new();
    }

    public interface ICandidateService
    {
        CandidateResult Search(CandidateQuery query);
        PeerCatalogItem Find(string candidateId);

        // Every trusted node's copy of the file with this hash: the same bytes, wherever they are.
        List<PeerCatalogItem> Copies(string blake3);
    }

    // Answers the Arr apps from what trusted peers currently offer. Media is matched by ID, except
    // where the Arr app has no ID to send: Whisparr searches for a scene by text (SceneQuery).
    // Quality, custom formats and upgrades are for the Arr app to judge from the release title.
    public class CandidateService : ICandidateService
    {
        public const int MaxLimit = 100;

        private const int MaxWords = 8;

        // No season number is this high, and nothing was broadcast before it.
        private const int FirstYear = 1900;

        private readonly IPeerCatalogRepository _items;
        private readonly IPeerRepository _peers;
        private readonly ILocalHoldingsFactory _holdings;
        private readonly IApplicationFactory _applications;

        public CandidateService(IPeerCatalogRepository items, IPeerRepository peers, ILocalHoldingsFactory holdings, IApplicationFactory applications)
        {
            _items = items;
            _peers = peers;
            _holdings = holdings;
            _applications = applications;
        }

        public CandidateResult Search(CandidateQuery query)
        {
            var limit = Math.Clamp(query.Limit, 1, MaxLimit);
            var offset = Math.Max(0, query.Offset);
            var trusted = TrustedPeerIds();
            var kinds = Kinds(query);

            if (trusted.Count == 0 || (kinds.Count == 0 && !query.IncludeAdult))
            {
                return new CandidateResult();
            }

            if (query.IsBrowse)
            {
                return query.RssApplicationId.HasValue
                    ? Arrivals(query, kinds, offset, limit)
                    : Newest(query, kinds, trusted, offset, limit);
            }

            var found = query.HasIds ? ByIds(query, FetchKinds(query, kinds)) : ByText(query, kinds);

            var airDates = new Dictionary<string, string>();

            var matches = OnePerFile(found)
                .Where(i => trusted.Contains(i.PeerId) && i.CandidateId != null)
                .Where(i => IsAsked(i, query, kinds))
                .Where(i =>
                {
                    if (!MatchesEpisode(i, query, out var airDate))
                    {
                        return false;
                    }

                    if (airDate != null)
                    {
                        airDates[i.CandidateId] = airDate;
                    }

                    return true;
                })
                .OrderBy(i => i.SeasonNumber)
                .ThenBy(i => i.GetEpisodeNumbers().FirstOrDefault())
                .ThenByDescending(i => i.Resolution)
                .ThenBy(i => i.CandidateId, StringComparer.Ordinal)
                .ToList();

            var page = matches.Skip(offset).Take(limit).ToList();

            return new CandidateResult
            {
                Total = matches.Count,
                Items = page,
                AirDates = page.Where(i => airDates.ContainsKey(i.CandidateId)).ToDictionary(i => i.CandidateId, i => airDates[i.CandidateId])
            };
        }

        public PeerCatalogItem Find(string candidateId)
        {
            if (candidateId.IsNullOrWhiteSpace())
            {
                return null;
            }

            var item = _items.FindByCandidateId(candidateId);

            // Rows go when trust ends, but a request can arrive before they have gone.
            return item != null && TrustedPeerIds().Contains(item.PeerId) ? item : null;
        }

        private HashSet<int> TrustedPeerIds()
        {
            return _peers.All().Where(p => p.State == PeerState.Trusted).Select(p => p.Id).ToHashSet();
        }

        private static List<MediaKind> Kinds(CandidateQuery query)
        {
            var kinds = new List<MediaKind>();

            if (query.IncludeTv)
            {
                kinds.Add(MediaKind.Tv);
            }

            if (query.IncludeMovies)
            {
                kinds.Add(MediaKind.Movie);
            }

            if (query.IncludeMusic)
            {
                kinds.Add(MediaKind.Music);
            }

            return kinds;
        }

        // Adult content is kept as movies, so finding it means looking among movies.
        private static List<MediaKind> FetchKinds(CandidateQuery query, List<MediaKind> kinds)
        {
            return query.IncludeAdult && !kinds.Contains(MediaKind.Movie) ? kinds.Append(MediaKind.Movie).ToList() : kinds;
        }

        // Adult content only for an adult search, and nothing else for one. An album only when its
        // tracks can be fetched.
        private static bool IsAsked(PeerCatalogItem item, CandidateQuery query, List<MediaKind> kinds)
        {
            if (item.IsAdult)
            {
                return query.IncludeAdult;
            }

            return kinds.Contains(item.MediaKind) && (item.MediaKind != MediaKind.Music || item.IsAlbum);
        }

        // The Arr apps send every ID they know, in one request. A file matches when any ID it
        // carries agrees, because not every Arr app knows every ID for a title.
        private IEnumerable<PeerCatalogItem> ByIds(CandidateQuery query, List<MediaKind> kinds)
        {
            var imdbId = NormalizeImdbId(query.ImdbId);

            return kinds.SelectMany(kind => _items.FindByMediaIds(kind, kind == MediaKind.Tv ? query.TvdbId : 0, query.TmdbId, imdbId));
        }

        private IEnumerable<PeerCatalogItem> ByText(CandidateQuery query, List<MediaKind> kinds)
        {
            var others = kinds.Where(k => k != MediaKind.Music).ToList();
            var found = others.Count > 0 && query.Text.IsNotNullOrWhiteSpace() ? ByWords(query.Text, others) : Enumerable.Empty<PeerCatalogItem>();

            if (query.IncludeMusic)
            {
                found = found.Concat(Albums(query.MusicText));
            }

            return query.IncludeAdult ? found.Concat(ScenesAndFilms(query.Text)) : found;
        }

        // Lidarr searches by artist and album. An album is found when every word of both is in
        // its artist, title, year or release title. Lidarr reads the title to judge the rest.
        private IEnumerable<PeerCatalogItem> Albums(string text)
        {
            var words = Words(text).Take(MaxWords).ToList();

            if (words.Count == 0)
            {
                return Enumerable.Empty<PeerCatalogItem>();
            }

            return _items.FindAlbumsByText(words.OrderByDescending(w => w.Length).First())
                .Where(i => i.IsAlbum)
                .Where(i =>
                {
                    var haystack = Words($"{i.Artist} {i.Title} {i.Year} {i.ReleaseTitle}").ToList();

                    return words.All(w => haystack.Any(h => h.Contains(w)));
                });
        }

        // Adult content by Whisparr's text search. A query with a day in it is narrowed by that
        // day, and any other by its longest word, before every word is checked.
        private IEnumerable<PeerCatalogItem> ScenesAndFilms(string text)
        {
            var scene = SceneQuery.Parse(text);

            if (scene.IsEmpty)
            {
                return Enumerable.Empty<PeerCatalogItem>();
            }

            var narrowed = scene.ReleaseDate != null
                ? _items.FindAdultByReleaseDate(scene.ReleaseDate)
                : _items.FindByText(scene.Words.OrderByDescending(w => w.Length).First());

            return narrowed.Where(scene.Matches);
        }

        private IEnumerable<PeerCatalogItem> ByWords(string text, List<MediaKind> kinds)
        {
            var words = Words(text).Take(MaxWords).ToList();

            if (words.Count == 0)
            {
                return Enumerable.Empty<PeerCatalogItem>();
            }

            // The database narrows by the longest word, and the rest are checked here.
            return _items.FindByText(words.OrderByDescending(w => w.Length).First())
                .Where(i => !i.IsAdult && kinds.Contains(i.MediaKind))
                .Where(i =>
                {
                    var haystack = Words($"{i.ReleaseTitle} {i.Title} {i.Year}").ToList();

                    return words.All(w => haystack.Any(h => h.Contains(w)));
                });
        }

        private CandidateResult Arrivals(CandidateQuery query, List<MediaKind> kinds, int offset, int limit)
        {
            var settings = _applications.All().FirstOrDefault(a => a.Id == query.RssApplicationId)?.Settings as ArrApplicationSettings;
            if (settings?.FetchNewFilesAutomatically != true)
            {
                return new CandidateResult();
            }

            var holdings = _holdings.LoadForApplication(query.RssApplicationId.Value);
            var wanted = _items.RssArrivals()
                .Where(i => i.CandidateId != null && IsAsked(i, query, kinds))
                .Where(i =>
                {
                    var status = holdings.Classify(i);
                    return status == PeerItemStatus.Missing || (query.IncludeUpgrades && settings.IncludeAutomaticUpgrades && status == PeerItemStatus.Upgrade);
                });
            var matches = OnePerFile(wanted).OrderByDescending(i => i.FirstSeen).ThenBy(i => i.CandidateId, StringComparer.Ordinal).ToList();

            return new CandidateResult { Total = matches.Count, Items = matches.Skip(offset).Take(limit).ToList() };
        }

        private CandidateResult Newest(CandidateQuery query, List<MediaKind> kinds, HashSet<int> trusted, int offset, int limit)
        {
            var peerIds = trusted.ToList();

            // The Arr apps page in whole steps of the limit they asked for.
            var spec = new PagingSpec<PeerCatalogItem>
            {
                Page = (offset / limit) + 1,
                PageSize = limit,
                SortKey = "dateAdded",
                SortDirection = SortDirection.Descending
            };

            spec.FilterExpressions.Add(i => peerIds.Contains(i.PeerId));

            // Adult content only when asked for, and otherwise only the kinds of file asked for.
            // Albums, and any kind of file from a newer node, are never listed for Sonarr or Radarr.
            if (!query.IncludeAdult)
            {
                spec.FilterExpressions.Add(i => i.IsAdult == false);
            }

            spec.FilterExpressions.Add(OfKinds(kinds, query.IncludeAdult));

            var page = _items.GetPaged(spec);

            // Copies that fall on the same page are shown once. The count is of every copy.
            return new CandidateResult { Total = page.TotalRecords, Items = OnePerFile(page.Records.Where(i => i.CandidateId != null)).ToList() };
        }

        // i => i.MediaKind == one of kinds || (adult && i.IsAdult), written out as the comparisons
        // the database query builder reads. An album that cannot be fetched is listed as nothing.
        private static Expression<Func<PeerCatalogItem, bool>> OfKinds(List<MediaKind> kinds, bool adult)
        {
            var item = Expression.Parameter(typeof(PeerCatalogItem), "i");
            Expression body = adult ? Expression.Equal(Expression.Property(item, nameof(PeerCatalogItem.IsAdult)), Expression.Constant(true)) : null;

            foreach (var kind in kinds)
            {
                Expression match = Expression.Equal(Expression.Convert(Expression.Property(item, nameof(PeerCatalogItem.MediaKind)), typeof(int)), Expression.Constant((int)kind));

                if (kind == MediaKind.Music)
                {
                    match = Expression.AndAlso(match, Expression.NotEqual(Expression.Property(item, nameof(PeerCatalogItem.Tracks)), Expression.Constant(null, typeof(string))));
                }

                body = body == null ? match : Expression.OrElse(body, match);
            }

            return Expression.Lambda<Func<PeerCatalogItem, bool>>(body ?? Expression.Equal(Expression.Property(item, nameof(PeerCatalogItem.Id)), Expression.Constant(-1)), item);
        }

        public List<PeerCatalogItem> Copies(string blake3)
        {
            if (blake3 == null)
            {
                return new List<PeerCatalogItem>();
            }

            var trusted = TrustedPeerIds();

            return _items.FindByBlake3(blake3).Where(i => trusted.Contains(i.PeerId) && i.CandidateId != null).ToList();
        }

        // The same file offered by several trusted nodes is one release to the Arr app, as one
        // release is one release however many indexers list it. The copy shown is always the same
        // one while the same nodes offer it, so its GUID holds still. A transfer of it can carry
        // on from any of the others. A file whose hash is not known yet stands alone, and so does
        // the same bytes offered as something else, such as another episode, so that each search
        // still finds what it asks for.
        private IEnumerable<PeerCatalogItem> OnePerFile(IEnumerable<PeerCatalogItem> items)
        {
            var trusted = TrustedPeerIds();

            return items
                .GroupBy(i => i.Blake3 != null && i.CandidateId != null && trusted.Contains(i.PeerId)
                    ? $"{i.Blake3}|{(int)i.MediaKind}|{i.TvdbId}|{i.TmdbId}|{i.SeasonNumber}|{i.EpisodeNumbers}|{i.ForeignAlbumId}"
                    : $"{i.PeerId}/{i.Id}")
                .Select(g => g.OrderBy(i => i.CandidateId, StringComparer.Ordinal).First());
        }

        // Sonarr asks in one of two ways, and which one depends on how the series is set up in
        // the Sonarr that is asking, not in the peer's. By number: a season, or a season and an
        // episode. By day, for a series it calls daily: a year, or a year and a day. A file found
        // by day comes back with that day in airDate.
        private static bool MatchesEpisode(PeerCatalogItem item, CandidateQuery query, out string airDate)
        {
            airDate = null;

            if (item.MediaKind != MediaKind.Tv)
            {
                return true;
            }

            var day = query.AirDate;

            if (day != null)
            {
                airDate = item.GetAirDates().Contains(day) ? day : null;

                return airDate != null;
            }

            // For a series the scene numbers differently, Sonarr asks by the scene's numbers. A
            // Sonarr that has no scene numbers for it asks by TVDB's, so either is a match. A file
            // found the other way than Sonarr meant is named in its title by the scene's numbers,
            // and Sonarr turns it down as not the episode it asked for.
            if (query.Episode.IsNotNullOrWhiteSpace())
            {
                if (!int.TryParse(query.Episode, out var episode))
                {
                    return false;
                }

                return ((!query.Season.HasValue || item.SeasonNumber == query.Season.Value) && item.GetEpisodeNumbers().Contains(episode)) ||
                       (item.HasSceneNumbers && (!query.Season.HasValue || item.SceneSeasonNumber == query.Season.Value) && item.GetSceneEpisodeNumbers().Contains(episode));
            }

            if (!query.Season.HasValue || item.SeasonNumber == query.Season.Value || (item.HasSceneNumbers && item.SceneSeasonNumber == query.Season.Value))
            {
                return true;
            }

            // A season that is a year, with no episode, is a whole year of a daily show. A series
            // whose seasons really are numbered by year was matched by number just above.
            if (query.Season.Value >= FirstYear)
            {
                var year = query.Season.Value.ToString("0000", CultureInfo.InvariantCulture) + "-";

                airDate = item.GetAirDates().FirstOrDefault(d => d.StartsWith(year, StringComparison.Ordinal));
            }

            return airDate != null;
        }

        // Peers send IMDb IDs as their Arr app holds them, "tt" and all. Radarr asks without it.
        private static string NormalizeImdbId(string imdbId)
        {
            var digits = new string((imdbId ?? string.Empty).Where(char.IsDigit).ToArray());

            return digits.Length > 0 ? "tt" + digits : null;
        }

        private static IEnumerable<string> Words(string text)
        {
            return new string((text ?? string.Empty).ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}

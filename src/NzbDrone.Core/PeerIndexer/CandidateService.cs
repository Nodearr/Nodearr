using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;
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

        // Sonarr asks for an episode of a daily show by the day it aired: the year as the season
        // and "MM/dd" as the episode. This is that day as yyyy-MM-dd, or null for any other query.
        public string AirDate =>
            Season.HasValue && DateTime.TryParseExact($"{Season.Value:0000}/{Episode}", "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null;

        public bool HasIds => TvdbId > 0 || TmdbId > 0 || ImdbId.IsNotNullOrWhiteSpace() || HasUnknownIds;

        // No IDs and no text: the Arr app's connection test, or its RSS sync.
        public bool IsBrowse => !HasIds && Text.IsNullOrWhiteSpace();
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
    }

    // Answers the Arr apps from what trusted peers currently offer. Media is matched by ID only.
    // Quality, custom formats and upgrades are for the Arr app to judge from the release title.
    public class CandidateService : ICandidateService
    {
        public const int MaxLimit = 100;

        private const int MaxWords = 8;

        // No season number is this high, and nothing was broadcast before it.
        private const int FirstYear = 1900;

        private readonly IPeerCatalogRepository _items;
        private readonly IPeerRepository _peers;

        public CandidateService(IPeerCatalogRepository items, IPeerRepository peers)
        {
            _items = items;
            _peers = peers;
        }

        public CandidateResult Search(CandidateQuery query)
        {
            var limit = Math.Clamp(query.Limit, 1, MaxLimit);
            var offset = Math.Max(0, query.Offset);
            var trusted = TrustedPeerIds();
            var kinds = Kinds(query);

            if (trusted.Count == 0 || kinds.Count == 0)
            {
                return new CandidateResult();
            }

            if (query.IsBrowse)
            {
                return Newest(kinds, trusted, offset, limit);
            }

            var found = query.HasIds ? ByIds(query, kinds) : ByText(query.Text, kinds);

            var airDates = new Dictionary<string, string>();

            var matches = found
                .Where(i => trusted.Contains(i.PeerId) && i.CandidateId != null)
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

            return kinds;
        }

        // The Arr apps send every ID they know, in one request. A file matches when any ID it
        // carries agrees, because not every Arr app knows every ID for a title.
        private IEnumerable<PeerCatalogItem> ByIds(CandidateQuery query, List<MediaKind> kinds)
        {
            var imdbId = NormalizeImdbId(query.ImdbId);

            return kinds.SelectMany(kind => _items.FindByMediaIds(kind, kind == MediaKind.Tv ? query.TvdbId : 0, query.TmdbId, imdbId));
        }

        private IEnumerable<PeerCatalogItem> ByText(string text, List<MediaKind> kinds)
        {
            var words = Words(text).Take(MaxWords).ToList();

            if (words.Count == 0)
            {
                return Enumerable.Empty<PeerCatalogItem>();
            }

            // The database narrows by the longest word, and the rest are checked here.
            return _items.FindByText(words.OrderByDescending(w => w.Length).First())
                .Where(i => kinds.Contains(i.MediaKind))
                .Where(i =>
                {
                    var haystack = Words($"{i.ReleaseTitle} {i.Title} {i.Year}").ToList();

                    return words.All(w => haystack.Any(h => h.Contains(w)));
                });
        }

        private CandidateResult Newest(List<MediaKind> kinds, HashSet<int> trusted, int offset, int limit)
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

            if (kinds.Count == 1)
            {
                var kind = kinds[0];

                spec.FilterExpressions.Add(i => i.MediaKind == kind);
            }

            var page = _items.GetPaged(spec);

            return new CandidateResult { Total = page.TotalRecords, Items = page.Records.Where(i => i.CandidateId != null).ToList() };
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

            if (query.Episode.IsNotNullOrWhiteSpace())
            {
                return (!query.Season.HasValue || item.SeasonNumber == query.Season.Value) &&
                       int.TryParse(query.Episode, out var episode) && item.GetEpisodeNumbers().Contains(episode);
            }

            if (!query.Season.HasValue || item.SeasonNumber == query.Season.Value)
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

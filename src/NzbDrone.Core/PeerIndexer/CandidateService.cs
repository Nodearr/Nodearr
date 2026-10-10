using System;
using System.Collections.Generic;
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

        public bool HasIds => TvdbId > 0 || TmdbId > 0 || ImdbId.IsNotNullOrWhiteSpace() || HasUnknownIds;

        // No IDs and no text: the Arr app's connection test, or its RSS sync.
        public bool IsBrowse => !HasIds && Text.IsNullOrWhiteSpace();
    }

    public class CandidateResult
    {
        public int Total { get; set; }
        public List<PeerCatalogItem> Items { get; set; } = new();
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

            var matches = found
                .Where(i => trusted.Contains(i.PeerId) && i.CandidateId != null)
                .Where(i => MatchesEpisode(i, query))
                .OrderBy(i => i.SeasonNumber)
                .ThenBy(i => i.GetEpisodeNumbers().FirstOrDefault())
                .ThenByDescending(i => i.Resolution)
                .ThenBy(i => i.CandidateId, StringComparer.Ordinal)
                .ToList();

            return new CandidateResult { Total = matches.Count, Items = matches.Skip(offset).Take(limit).ToList() };
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

        private static bool MatchesEpisode(PeerCatalogItem item, CandidateQuery query)
        {
            if (item.MediaKind != MediaKind.Tv)
            {
                return true;
            }

            if (query.Season.HasValue && item.SeasonNumber != query.Season.Value)
            {
                return false;
            }

            if (query.Episode.IsNullOrWhiteSpace())
            {
                return true;
            }

            // Anything that is not a number is a date, which is how the Arr apps ask for daily
            // shows. Peers are not told air dates, so those match nothing.
            return int.TryParse(query.Episode, out var episode) && item.GetEpisodeNumbers().Contains(episode);
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

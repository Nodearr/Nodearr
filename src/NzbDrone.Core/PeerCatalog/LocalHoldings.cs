using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.PeerCatalog
{
    public interface ILocalHoldingsFactory
    {
        LocalHoldings Load();
    }

    public class LocalHoldingsFactory : ILocalHoldingsFactory
    {
        private readonly ICatalogSeriesRepository _series;
        private readonly ICatalogEpisodeRepository _episodes;
        private readonly ICatalogEpisodeFileRepository _episodeFiles;
        private readonly ICatalogMovieRepository _movies;
        private readonly ICatalogMovieFileRepository _movieFiles;

        public LocalHoldingsFactory(ICatalogSeriesRepository series,
                                    ICatalogEpisodeRepository episodes,
                                    ICatalogEpisodeFileRepository episodeFiles,
                                    ICatalogMovieRepository movies,
                                    ICatalogMovieFileRepository movieFiles)
        {
            _series = series;
            _episodes = episodes;
            _episodeFiles = episodeFiles;
            _movies = movies;
            _movieFiles = movieFiles;
        }

        public LocalHoldings Load()
        {
            return new LocalHoldings(_series.All(), _episodes.All(), _episodeFiles.All(), _movies.All(), _movieFiles.All());
        }
    }

    // A snapshot of what the local Arr apps hold, arranged for comparing a peer's files against it.
    // Matching is by the IDs the Arr apps use for the series or movie, never by title.
    public class LocalHoldings
    {
        private static readonly Dictionary<string, int> SourceRanks = new(StringComparer.OrdinalIgnoreCase)
        {
            { "bluray", 5 },
            { "blurayRaw", 5 },
            { "web", 4 },
            { "webdl", 4 },
            { "webRip", 3 },
            { "television", 2 },
            { "televisionRaw", 2 },
            { "tv", 2 },
            { "dvd", 1 }
        };

        private readonly ILookup<string, CatalogSeries> _seriesByExternalId;
        private readonly ILookup<(int, int, int, int), CatalogEpisode> _episodes;
        private readonly Dictionary<(int, int), CatalogEpisodeFile> _episodeFiles;
        private readonly ILookup<string, CatalogMovie> _moviesByExternalId;
        private readonly Dictionary<(int, int), CatalogMovieFile> _movieFiles;

        public LocalHoldings(IEnumerable<CatalogSeries> series,
                             IEnumerable<CatalogEpisode> episodes,
                             IEnumerable<CatalogEpisodeFile> episodeFiles,
                             IEnumerable<CatalogMovie> movies,
                             IEnumerable<CatalogMovieFile> movieFiles)
        {
            // An item the Arr app no longer reports is not something this node holds.
            _seriesByExternalId = series.Where(s => s.MissingSince == null)
                .SelectMany(s => ExternalIds(s.TvdbId, s.TmdbId, s.ImdbId).Select(id => (id, s)))
                .ToLookup(x => x.id, x => x.s);

            _episodes = episodes.Where(e => e.MissingSince == null)
                .ToLookup(e => (e.ApplicationId, e.ArrSeriesId, e.SeasonNumber, e.EpisodeNumber));

            _episodeFiles = episodeFiles.Where(f => f.MissingSince == null)
                .ToDictionary(f => (f.ApplicationId, f.ArrId));

            _moviesByExternalId = movies.Where(m => m.MissingSince == null)
                .SelectMany(m => ExternalIds(0, m.TmdbId, m.ImdbId).Select(id => (id, m)))
                .ToLookup(x => x.id, x => x.m);

            _movieFiles = movieFiles.Where(f => f.MissingSince == null)
                .ToDictionary(f => (f.ApplicationId, f.ArrId));
        }

        public PeerItemStatus Classify(PeerCatalogItem item)
        {
            return item.MediaKind == MediaKind.Movie ? ClassifyMovie(item) : ClassifyEpisodes(item);
        }

        private PeerItemStatus ClassifyMovie(PeerCatalogItem item)
        {
            // Radarr knows a movie by its TMDb ID. The IMDb ID is the fallback.
            var movies = Find(_moviesByExternalId, ExternalIds(0, item.TmdbId, item.ImdbId));

            if (movies.Count == 0)
            {
                return PeerItemStatus.NotInLibrary;
            }

            var files = movies.Select(m => _movieFiles.GetValueOrDefault((m.ApplicationId, m.ArrMovieFileId))).Where(f => f != null).ToList();

            return Compare(item, files);
        }

        private PeerItemStatus ClassifyEpisodes(PeerCatalogItem item)
        {
            var series = Find(_seriesByExternalId, ExternalIds(item.TvdbId, item.TmdbId, item.ImdbId));

            if (series.Count == 0)
            {
                return PeerItemStatus.NotInLibrary;
            }

            var result = PeerItemStatus.Have;

            // A file that holds several episodes is only "had" when every one of them is.
            foreach (var number in item.GetEpisodeNumbers())
            {
                var files = series
                    .SelectMany(s => _episodes[(s.ApplicationId, s.ArrId, item.SeasonNumber, number)])
                    .Select(e => _episodeFiles.GetValueOrDefault((e.ApplicationId, e.ArrEpisodeFileId)))
                    .Where(f => f != null)
                    .ToList();

                var status = Compare(item, files);

                if (status < result)
                {
                    result = status;
                }
            }

            return result;
        }

        private static PeerItemStatus Compare(PeerCatalogItem item, List<CatalogEpisodeFile> local)
        {
            return Compare(item, local.Select(f => (f.Resolution, f.QualitySource)).ToList());
        }

        private static PeerItemStatus Compare(PeerCatalogItem item, List<CatalogMovieFile> local)
        {
            return Compare(item, local.Select(f => (f.Resolution, f.QualitySource)).ToList());
        }

        // A deliberately simple idea of "better": higher resolution, or the same resolution from a
        // better source. The Arr apps make the real decision with their own profiles when they search.
        private static PeerItemStatus Compare(PeerCatalogItem item, List<(int Resolution, string Source)> local)
        {
            if (local.Count == 0)
            {
                return PeerItemStatus.Missing;
            }

            var isBetterThanAll = local.All(l =>
                item.Resolution > l.Resolution ||
                (item.Resolution == l.Resolution && Rank(item.QualitySource) > Rank(l.Source)));

            return isBetterThanAll ? PeerItemStatus.Upgrade : PeerItemStatus.Have;
        }

        private static int Rank(string source)
        {
            return source != null && SourceRanks.TryGetValue(source, out var rank) ? rank : 0;
        }

        private static List<T> Find<T>(ILookup<string, T> lookup, IEnumerable<string> ids)
        {
            // The first ID that matches anything decides, in order of how much the Arr app trusts it.
            foreach (var id in ids)
            {
                var found = lookup[id].ToList();

                if (found.Count > 0)
                {
                    return found;
                }
            }

            return new List<T>();
        }

        private static IEnumerable<string> ExternalIds(int tvdbId, int tmdbId, string imdbId)
        {
            if (tvdbId > 0)
            {
                yield return "tvdb:" + tvdbId;
            }

            if (tmdbId > 0)
            {
                yield return "tmdb:" + tmdbId;
            }

            if (imdbId.IsNotNullOrWhiteSpace())
            {
                yield return "imdb:" + imdbId.ToLowerInvariant();
            }
        }
    }
}

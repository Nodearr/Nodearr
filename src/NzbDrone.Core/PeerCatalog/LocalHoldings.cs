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
        private readonly ICatalogQualityProfileRepository _profiles;

        public LocalHoldingsFactory(ICatalogSeriesRepository series,
                                    ICatalogEpisodeRepository episodes,
                                    ICatalogEpisodeFileRepository episodeFiles,
                                    ICatalogMovieRepository movies,
                                    ICatalogMovieFileRepository movieFiles,
                                    ICatalogQualityProfileRepository profiles)
        {
            _series = series;
            _episodes = episodes;
            _episodeFiles = episodeFiles;
            _movies = movies;
            _movieFiles = movieFiles;
            _profiles = profiles;
        }

        public LocalHoldings Load()
        {
            return new LocalHoldings(_series.All(), _episodes.All(), _episodeFiles.All(), _movies.All(), _movieFiles.All(), _profiles.All());
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

        // Whisparr's scenes by studio and release date. Whisparr v2 and v3 know a scene by different
        // IDs, and agree on these.
        private readonly ILookup<string, CatalogMovie> _scenesByStudioAndDay;

        // By application and the Arr app's profile ID. Only profiles whose ranking has been read.
        private readonly Dictionary<(int, int), QualityRanking> _rankings;

        public LocalHoldings(IEnumerable<CatalogSeries> series,
                             IEnumerable<CatalogEpisode> episodes,
                             IEnumerable<CatalogEpisodeFile> episodeFiles,
                             IEnumerable<CatalogMovie> movies,
                             IEnumerable<CatalogMovieFile> movieFiles,
                             IEnumerable<CatalogQualityProfile> profiles = null)
        {
            _rankings = (profiles ?? Enumerable.Empty<CatalogQualityProfile>())
                .Where(p => p.MissingSince == null)
                .Select(p => (Key: (p.ApplicationId, p.ArrId), Ranking: QualityRanking.FromStored(p.QualityRanking)))
                .Where(x => x.Ranking != null)
                .GroupBy(x => x.Key)
                .ToDictionary(g => g.Key, g => g.First().Ranking);

            // An item the Arr app no longer reports is not something this node holds.
            _seriesByExternalId = series.Where(s => s.MissingSince == null)
                .SelectMany(s => ExternalIds(s.TvdbId, s.TmdbId, s.ImdbId).Select(id => (id, s)))
                .ToLookup(x => x.id, x => x.s);

            _episodes = episodes.Where(e => e.MissingSince == null)
                .ToLookup(e => (e.ApplicationId, e.ArrSeriesId, e.SeasonNumber, e.EpisodeNumber));

            _episodeFiles = episodeFiles.Where(f => f.MissingSince == null)
                .ToDictionary(f => (f.ApplicationId, f.ArrId));

            _moviesByExternalId = movies.Where(m => m.MissingSince == null)
                .SelectMany(m => WhisparrIds(m.ForeignId, m.StashId, m.TpdbId).Concat(ExternalIds(0, m.TmdbId, m.ImdbId)).Select(id => (id, m)))
                .ToLookup(x => x.id, x => x.m);

            _movieFiles = movieFiles.Where(f => f.MissingSince == null)
                .ToDictionary(f => (f.ApplicationId, f.ArrId));

            _scenesByStudioAndDay = movies
                .Where(m => m.MissingSince == null && m.ItemType == "scene" && StudioAndDay(m.Studio, m.ReleaseDate) != null)
                .ToLookup(m => StudioAndDay(m.Studio, m.ReleaseDate));
        }

        public PeerItemStatus Classify(PeerCatalogItem item)
        {
            return item.MediaKind == MediaKind.Movie ? ClassifyMovie(item) : ClassifyEpisodes(item);
        }

        private PeerItemStatus ClassifyMovie(PeerCatalogItem item)
        {
            // Radarr knows a movie by its TMDb ID. The IMDb ID is the fallback. Whisparr knows a
            // film or a scene by its own IDs first.
            var movies = Find(_moviesByExternalId, WhisparrIds(item.ForeignId, item.StashId, item.TpdbId).Concat(ExternalIds(0, item.TmdbId, item.ImdbId)));

            // A scene from the other version of Whisparr, known here by other IDs.
            if (movies.Count == 0 && item.IsScene)
            {
                var key = StudioAndDay(item.Studio, item.ReleaseDate);

                movies = key == null ? movies : _scenesByStudioAndDay[key].ToList();
            }

            if (movies.Count == 0)
            {
                return PeerItemStatus.NotInLibrary;
            }

            var files = movies
                .Select(m => (File: (CatalogFile)_movieFiles.GetValueOrDefault((m.ApplicationId, m.ArrMovieFileId)), Ranking: RankingFor(m.ApplicationId, m.QualityProfileId)))
                .Where(x => x.File != null)
                .ToList();

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
                    .SelectMany(s => _episodes[(s.ApplicationId, s.ArrId, item.SeasonNumber, number)]
                        .Select(e => (File: (CatalogFile)_episodeFiles.GetValueOrDefault((e.ApplicationId, e.ArrEpisodeFileId)), Ranking: RankingFor(s.ApplicationId, s.QualityProfileId))))
                    .Where(x => x.File != null)
                    .ToList();

                var status = Compare(item, files);

                if (status < result)
                {
                    result = status;
                }
            }

            return result;
        }

        private QualityRanking RankingFor(int applicationId, int profileId)
        {
            return _rankings.GetValueOrDefault((applicationId, profileId));
        }

        // An upgrade is a file every local copy's Arr app would take in place of the one it has.
        private static PeerItemStatus Compare(PeerCatalogItem item, List<(CatalogFile File, QualityRanking Ranking)> local)
        {
            if (local.Count == 0)
            {
                return PeerItemStatus.Missing;
            }

            return local.All(l => IsBetter(item, l.File, l.Ranking)) ? PeerItemStatus.Upgrade : PeerItemStatus.Have;
        }

        // As the Arr app's quality profile judges it, where the profile's ranking is known. Before
        // the profiles are next read it is not, and a simple rule stands in: a higher resolution, or
        // the same resolution from a better source.
        private static bool IsBetter(PeerCatalogItem item, CatalogFile local, QualityRanking ranking)
        {
            if (ranking != null && item.Quality.IsNotNullOrWhiteSpace())
            {
                return ranking.IsUpgrade(local.Quality, item.Quality);
            }

            return item.Resolution > local.Resolution ||
                   (item.Resolution == local.Resolution && Rank(item.QualitySource) > Rank(local.QualitySource));
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

        // Couch Casting-X and CouchCastingX are one studio. Null when either is not known.
        private static string StudioAndDay(string studio, string releaseDate)
        {
            var letters = new string((studio ?? string.Empty).RemoveDiacritics().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

            return letters.Length == 0 || releaseDate.IsNullOrWhiteSpace() ? null : $"{letters}|{releaseDate}";
        }

        private static IEnumerable<string> WhisparrIds(string foreignId, string stashId, string tpdbId)
        {
            if (foreignId.IsNotNullOrWhiteSpace())
            {
                yield return "foreign:" + foreignId.ToLowerInvariant();
            }

            if (stashId.IsNotNullOrWhiteSpace())
            {
                yield return "stash:" + stashId.ToLowerInvariant();
            }

            if (tpdbId.IsNotNullOrWhiteSpace())
            {
                yield return "tpdb:" + tpdbId.ToLowerInvariant();
            }
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

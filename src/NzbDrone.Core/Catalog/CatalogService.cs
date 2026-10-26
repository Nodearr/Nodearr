using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NLog;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Catalog
{
    // A series or movie in this node's own catalog, with how many files its Arr app reports.
    public class CatalogTitle
    {
        public int ApplicationId { get; set; }
        public string Kind { get; set; }
        public bool IsAdult { get; set; }
        public PosterSubject Subject { get; set; }
        public int ArrId { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public int ArtistArrId { get; set; }
        public string SortTitle { get; set; }
        public int Year { get; set; }
        public int Files { get; set; }
    }

    public class CatalogApplicationSummary
    {
        public int ApplicationId { get; set; }
        public string ApplicationName { get; set; }
        public string Implementation { get; set; }
        public int Series { get; set; }
        public int Episodes { get; set; }
        public int EpisodeFiles { get; set; }
        public int Movies { get; set; }
        public int MovieFiles { get; set; }
        public int Artists { get; set; }
        public int Albums { get; set; }
        public int TrackFiles { get; set; }
        public int MissingFiles { get; set; }
        public DateTime? LastSyncStarted { get; set; }
        public DateTime? LastSyncCompleted { get; set; }
        public string LastSyncError { get; set; }
    }

    public interface ICatalogService
    {
        void Refresh(int? applicationId, bool purgeMissing);

        // Reads one series, movie or artist again, and nothing else of the Arr app's library.
        void RefreshTitle(int applicationId, int arrId);
        List<CatalogApplicationSummary> GetSummaries();
        PagingSpec<CatalogSeries> PagedSeries(PagingSpec<CatalogSeries> pagingSpec);
        PagingSpec<CatalogEpisodeFile> PagedEpisodeFiles(PagingSpec<CatalogEpisodeFile> pagingSpec);
        PagingSpec<CatalogMovieFile> PagedMovieFiles(PagingSpec<CatalogMovieFile> pagingSpec, string kind = null);
        PagingSpec<CatalogTrackFile> PagedTrackFiles(PagingSpec<CatalogTrackFile> pagingSpec);
        List<CatalogSeries> GetSeries(int applicationId, IEnumerable<int> arrSeriesIds);
        List<CatalogEpisode> GetEpisodes(int applicationId, IEnumerable<int> arrEpisodeIds);
        List<CatalogEpisode> GetEpisodesForFiles(int applicationId, IEnumerable<int> arrEpisodeFileIds);
        List<CatalogEpisode> GetEpisodesForSeries(int applicationId, IEnumerable<int> arrSeriesIds);
        List<CatalogEpisodeFile> GetEpisodeFilesForSeries(int applicationId, IEnumerable<int> arrSeriesIds);
        List<CatalogMovieFile> GetMovieFiles(int applicationId, IEnumerable<int> arrMovieFileIds);
        List<CatalogMovie> GetMovies(int applicationId, IEnumerable<int> arrMovieIds);
        List<CatalogAlbum> GetAlbums(int applicationId, IEnumerable<int> arrAlbumIds);
        List<CatalogArtist> GetArtists(int applicationId, IEnumerable<int> arrArtistIds);

        // Every series and movie with a file, by sort title.
        List<CatalogTitle> GetTitles(int? applicationId);
    }

    public class CatalogService : ICatalogService,
                                  IExecute<CatalogRefreshCommand>,
                                  IExecute<CatalogTitleRefreshCommand>,
                                  IHandle<ProviderDeletedEvent<IApplication>>
    {
        // How long an item the Arr app no longer reports is kept before it is removed.
        public static readonly TimeSpan MissingRetention = TimeSpan.FromDays(7);

        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ComparedProperties = new();
        private static readonly object RefreshLock = new();

        private readonly IApplicationFactory _applicationFactory;
        private readonly IApplicationStatusService _applicationStatusService;
        private readonly ICatalogQualityProfileRepository _profiles;
        private readonly ICatalogSeriesRepository _series;
        private readonly ICatalogEpisodeRepository _episodes;
        private readonly ICatalogEpisodeFileRepository _episodeFiles;
        private readonly ICatalogMovieRepository _movies;
        private readonly ICatalogMovieFileRepository _movieFiles;
        private readonly ICatalogArtistRepository _artists;
        private readonly ICatalogAlbumRepository _albums;
        private readonly ICatalogTrackFileRepository _trackFiles;
        private readonly ICatalogSyncStatusRepository _syncStatus;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public CatalogService(IApplicationFactory applicationFactory,
                              IApplicationStatusService applicationStatusService,
                              ICatalogQualityProfileRepository profiles,
                              ICatalogSeriesRepository series,
                              ICatalogEpisodeRepository episodes,
                              ICatalogEpisodeFileRepository episodeFiles,
                              ICatalogMovieRepository movies,
                              ICatalogMovieFileRepository movieFiles,
                              ICatalogArtistRepository artists,
                              ICatalogAlbumRepository albums,
                              ICatalogTrackFileRepository trackFiles,
                              ICatalogSyncStatusRepository syncStatus,
                              IEventAggregator eventAggregator,
                              Logger logger)
        {
            _applicationFactory = applicationFactory;
            _applicationStatusService = applicationStatusService;
            _profiles = profiles;
            _series = series;
            _episodes = episodes;
            _episodeFiles = episodeFiles;
            _movies = movies;
            _movieFiles = movieFiles;
            _artists = artists;
            _albums = albums;
            _trackFiles = trackFiles;
            _syncStatus = syncStatus;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Execute(CatalogRefreshCommand message)
        {
            Refresh(message.ApplicationId, message.PurgeMissing);
        }

        public void Refresh(int? applicationId, bool purgeMissing)
        {
            var applications = _applicationFactory.SyncEnabled()
                .Where(a => ((ApplicationDefinition)a.Definition).HoldsLibrary)
                .Where(a => !applicationId.HasValue || a.Definition.Id == applicationId.Value)
                .ToList();

            foreach (var application in applications)
            {
                // Saving an application queues a refresh, and so can the user or the schedule. Two of
                // them merging the same catalog at once would both try to insert the same rows.
                lock (RefreshLock)
                {
                    Refresh(application, purgeMissing);
                }
            }
        }

        public void Execute(CatalogTitleRefreshCommand message)
        {
            RefreshTitle(message.ApplicationId, message.ArrId);
        }

        public void RefreshTitle(int applicationId, int arrId)
        {
            var application = _applicationFactory.SyncEnabled().FirstOrDefault(a => a.Definition.Id == applicationId);

            if (application == null)
            {
                return;
            }

            if (((ApplicationDefinition)application.Definition).RefreshesWhole)
            {
                Refresh(application, false);

                return;
            }

            ApplicationCatalog catalog;

            try
            {
                catalog = application.FetchTitle(arrId);
            }
            catch (Exception ex)
            {
                // The next full read catches up.
                _logger.Warn(ex, "Unable to read item {0} from {1}", arrId, application.Definition.Name);

                return;
            }

            var now = DateTime.UtcNow;
            var purgeBefore = now - MissingRetention;

            var definition = (ApplicationDefinition)application.Definition;

            lock (RefreshLock)
            {
                // Only this title's rows are compared, so everything else the Arr app has is left alone.
                if (definition.HoldsMusic)
                {
                    Merge(_artists, applicationId, catalog.Artists, now, purgeBefore, a => a.ArrId == arrId);
                    Merge(_albums, applicationId, catalog.Albums, now, purgeBefore, a => a.ArrArtistId == arrId);
                    Merge(_trackFiles, applicationId, catalog.TrackFiles, now, purgeBefore, f => f.ArrArtistId == arrId);
                }
                else if (definition.HoldsMovies)
                {
                    Merge(_movies, applicationId, catalog.Movies, now, purgeBefore, m => m.ArrId == arrId);
                    Merge(_movieFiles, applicationId, catalog.MovieFiles, now, purgeBefore, f => f.ArrMovieId == arrId);
                }
                else
                {
                    Merge(_series, applicationId, catalog.Series, now, purgeBefore, s => s.ArrId == arrId);
                    Merge(_episodes, applicationId, catalog.Episodes, now, purgeBefore, e => e.ArrSeriesId == arrId);
                    Merge(_episodeFiles, applicationId, catalog.EpisodeFiles, now, purgeBefore, f => f.ArrSeriesId == arrId);
                }
            }

            _eventAggregator.PublishEvent(new CatalogRefreshedEvent(applicationId));

            _logger.Debug("Read {0} {1} again from {2}: {3} files", definition.HoldsMusic ? "artist" : definition.HoldsMovies ? "movie" : "series", arrId, application.Definition.Name, catalog.EpisodeFiles.Count + catalog.MovieFiles.Count + catalog.TrackFiles.Count);
        }

        public void Handle(ProviderDeletedEvent<IApplication> message)
        {
            DeleteForApplication(message.ProviderId);
        }

        public List<CatalogApplicationSummary> GetSummaries()
        {
            return _applicationFactory.All().Where(definition => definition.HoldsLibrary).Select(definition =>
            {
                var status = _syncStatus.ForApplication(definition.Id);

                return new CatalogApplicationSummary
                {
                    ApplicationId = definition.Id,
                    ApplicationName = definition.Name,
                    Implementation = definition.Implementation,
                    Series = _series.Count(definition.Id, false),
                    Episodes = _episodes.Count(definition.Id, false),
                    EpisodeFiles = _episodeFiles.Count(definition.Id, false),
                    Movies = _movies.Count(definition.Id, false),
                    MovieFiles = _movieFiles.Count(definition.Id, false),
                    Artists = _artists.Count(definition.Id, false),
                    Albums = _albums.Count(definition.Id, false),
                    TrackFiles = _trackFiles.Count(definition.Id, false),
                    MissingFiles = _episodeFiles.Count(definition.Id, true) + _movieFiles.Count(definition.Id, true) + _trackFiles.Count(definition.Id, true),
                    LastSyncStarted = status?.LastSyncStarted,
                    LastSyncCompleted = status?.LastSyncCompleted,
                    LastSyncError = status?.LastSyncError
                };
            }).ToList();
        }

        public PagingSpec<CatalogEpisodeFile> PagedEpisodeFiles(PagingSpec<CatalogEpisodeFile> pagingSpec)
        {
            return _episodeFiles.GetPaged(pagingSpec);
        }

        public PagingSpec<CatalogSeries> PagedSeries(PagingSpec<CatalogSeries> pagingSpec)
        {
            return _series.GetPaged(pagingSpec);
        }

        public PagingSpec<CatalogMovieFile> PagedMovieFiles(PagingSpec<CatalogMovieFile> pagingSpec, string kind = null)
        {
            return kind == null ? _movieFiles.GetPaged(pagingSpec) : _movieFiles.GetPagedByKind(pagingSpec, kind);
        }

        public PagingSpec<CatalogTrackFile> PagedTrackFiles(PagingSpec<CatalogTrackFile> pagingSpec)
        {
            return _trackFiles.GetPaged(pagingSpec);
        }

        public List<CatalogSeries> GetSeries(int applicationId, IEnumerable<int> arrSeriesIds)
        {
            return _series.ForArrIds(applicationId, arrSeriesIds);
        }

        public List<CatalogEpisode> GetEpisodes(int applicationId, IEnumerable<int> arrEpisodeIds)
        {
            return _episodes.ForArrIds(applicationId, arrEpisodeIds);
        }

        public List<CatalogEpisode> GetEpisodesForFiles(int applicationId, IEnumerable<int> arrEpisodeFileIds)
        {
            return _episodes.ForFiles(applicationId, arrEpisodeFileIds);
        }

        public List<CatalogEpisode> GetEpisodesForSeries(int applicationId, IEnumerable<int> arrSeriesIds)
        {
            return _episodes.ForSeries(applicationId, arrSeriesIds);
        }

        public List<CatalogEpisodeFile> GetEpisodeFilesForSeries(int applicationId, IEnumerable<int> arrSeriesIds)
        {
            return _episodeFiles.ForSeries(applicationId, arrSeriesIds);
        }

        public List<CatalogMovieFile> GetMovieFiles(int applicationId, IEnumerable<int> arrMovieFileIds)
        {
            return _movieFiles.ForArrIds(applicationId, arrMovieFileIds);
        }

        public List<CatalogMovie> GetMovies(int applicationId, IEnumerable<int> arrMovieIds)
        {
            return _movies.ForArrIds(applicationId, arrMovieIds);
        }

        public List<CatalogAlbum> GetAlbums(int applicationId, IEnumerable<int> arrAlbumIds)
        {
            return _albums.ForArrIds(applicationId, arrAlbumIds);
        }

        public List<CatalogArtist> GetArtists(int applicationId, IEnumerable<int> arrArtistIds)
        {
            return _artists.ForArrIds(applicationId, arrArtistIds);
        }

        // The files are counted by the database. Titles are few beside the files.
        public List<CatalogTitle> GetTitles(int? applicationId)
        {
            var series = _series.All().Where(s => s.MissingSince == null).ToDictionary(s => (s.ApplicationId, s.ArrId));
            var movies = _movies.All().Where(m => m.MissingSince == null).ToDictionary(m => (m.ApplicationId, m.ArrId));
            var albums = _albums.All().Where(a => a.MissingSince == null).ToDictionary(a => (a.ApplicationId, a.ArrId));
            var artists = _artists.All().Where(a => a.MissingSince == null).ToDictionary(a => (a.ApplicationId, a.ArrId));
            var titles = new List<CatalogTitle>();

            foreach (var count in _episodeFiles.FilesPerSeries())
            {
                if (series.TryGetValue((count.ApplicationId, count.ArrId), out var s))
                {
                    titles.Add(new CatalogTitle { ApplicationId = count.ApplicationId, Kind = "series", Subject = PosterSubject.Series, ArrId = count.ArrId, Title = s.Title, SortTitle = s.SortTitle, Year = s.Year, Files = count.Files });
                }
            }

            foreach (var count in _movieFiles.FilesPerMovie())
            {
                if (movies.TryGetValue((count.ApplicationId, count.ArrId), out var m))
                {
                    titles.Add(new CatalogTitle
                    {
                        ApplicationId = count.ApplicationId,
                        Kind = m.IsAdult && m.ItemType == "scene" ? "scenes" : "movies",
                        IsAdult = m.IsAdult,
                        Subject = PosterSubject.Movie,
                        ArrId = count.ArrId,
                        Title = m.Title,
                        SortTitle = m.SortTitle,
                        Year = m.Year,
                        Files = count.Files
                    });
                }
            }

            foreach (var count in _trackFiles.FilesPerAlbum())
            {
                if (albums.TryGetValue((count.ApplicationId, count.ArrId), out var album))
                {
                    artists.TryGetValue((album.ApplicationId, album.ArrArtistId), out var artist);
                    titles.Add(new CatalogTitle
                    {
                        ApplicationId = count.ApplicationId,
                        Kind = "music",
                        Subject = PosterSubject.Album,
                        ArrId = count.ArrId,
                        Title = album.Title,
                        Artist = artist?.Name,
                        ArtistArrId = album.ArrArtistId,
                        SortTitle = album.Title,
                        Year = album.Year,
                        Files = count.Files
                    });
                }
            }

            return titles
                .Where(t => applicationId == null || t.ApplicationId == applicationId)
                .OrderBy(t => t.SortTitle ?? t.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Year)
                .ToList();
        }

        private void Refresh(IApplication application, bool purgeMissing)
        {
            var applicationId = application.Definition.Id;
            var status = _syncStatus.ForApplication(applicationId) ?? new CatalogSyncStatus { ApplicationId = applicationId };

            status.LastSyncStarted = DateTime.UtcNow;
            _syncStatus.Upsert(status);

            ApplicationCatalog catalog;

            try
            {
                catalog = application.FetchCatalog();
                _applicationStatusService.RecordSuccess(applicationId);
            }
            catch (Exception ex)
            {
                // A failed fetch says nothing about what the Arr app has, so nothing is marked missing.
                _logger.Warn(ex, "Unable to read the catalog from {0}", application.Definition.Name);
                _applicationStatusService.RecordFailure(applicationId);

                status.LastSyncError = ex.Message;
                _syncStatus.Upsert(status);

                return;
            }

            var now = DateTime.UtcNow;
            var purgeBefore = purgeMissing ? now : now - MissingRetention;

            Merge(_profiles, applicationId, catalog.QualityProfiles, now, purgeBefore);
            Merge(_series, applicationId, catalog.Series, now, purgeBefore);
            Merge(_episodes, applicationId, catalog.Episodes, now, purgeBefore);
            Merge(_episodeFiles, applicationId, catalog.EpisodeFiles, now, purgeBefore);
            Merge(_movies, applicationId, catalog.Movies, now, purgeBefore);
            Merge(_movieFiles, applicationId, catalog.MovieFiles, now, purgeBefore);
            Merge(_artists, applicationId, catalog.Artists, now, purgeBefore);
            Merge(_albums, applicationId, catalog.Albums, now, purgeBefore);
            Merge(_trackFiles, applicationId, catalog.TrackFiles, now, purgeBefore);

            status.LastSyncCompleted = now;
            status.LastSyncError = null;
            _syncStatus.Upsert(status);
            _eventAggregator.PublishEvent(new CatalogRefreshedEvent(applicationId));

            _logger.Info("Catalog for {0} refreshed: {1} series, {2} episodes, {3} episode files, {4} movies, {5} movie files, {6} artists, {7} albums, {8} track files",
                application.Definition.Name,
                catalog.Series.Count,
                catalog.Episodes.Count,
                catalog.EpisodeFiles.Count,
                catalog.Movies.Count,
                catalog.MovieFiles.Count,
                catalog.Artists.Count,
                catalog.Albums.Count,
                catalog.TrackFiles.Count);
        }

        // Inserts what is new, updates what changed, and marks what the Arr app stopped reporting.
        // Items missing since before purgeBefore are removed.
        // A scope limits the comparison to some of the application's rows, such as one series.
        private static void Merge<TModel>(ICatalogRepository<TModel> repository, int applicationId, List<TModel> incoming, DateTime now, DateTime purgeBefore, Func<TModel, bool> scope = null)
            where TModel : CatalogModel, new()
        {
            var existing = repository.ForApplication(applicationId).Where(scope ?? (_ => true)).ToDictionary(x => x.ArrId);
            var inserts = new List<TModel>();
            var updates = new List<TModel>();

            foreach (var item in incoming.GroupBy(x => x.ArrId).Select(g => g.First()))
            {
                item.ApplicationId = applicationId;

                if (existing.Remove(item.ArrId, out var current))
                {
                    item.Id = current.Id;

                    if (!SameValues(current, item))
                    {
                        updates.Add(item);
                    }
                }
                else
                {
                    inserts.Add(item);
                }
            }

            var purge = new List<int>();

            foreach (var gone in existing.Values)
            {
                if (gone.MissingSince == null)
                {
                    gone.MissingSince = now;
                    updates.Add(gone);
                }
                else if (gone.MissingSince <= purgeBefore)
                {
                    purge.Add(gone.Id);
                }
            }

            repository.InsertMany(inserts);
            repository.UpdateMany(updates);
            repository.DeleteMany(purge);
        }

        private static bool SameValues<TModel>(TModel left, TModel right)
        {
            var properties = ComparedProperties.GetOrAdd(typeof(TModel), type => type.GetProperties().Where(p => p.CanRead && p.CanWrite).ToArray());

            foreach (var property in properties)
            {
                var a = property.GetValue(left);
                var b = property.GetValue(right);

                // Dates lose sub-second precision in storage.
                if (a is DateTime first && b is DateTime second)
                {
                    if (Math.Abs((first.ToUniversalTime() - second.ToUniversalTime()).TotalSeconds) >= 1)
                    {
                        return false;
                    }
                }
                else if (!Equals(a ?? string.Empty, b ?? string.Empty))
                {
                    return false;
                }
            }

            return true;
        }

        private void DeleteForApplication(int applicationId)
        {
            _profiles.DeleteForApplication(applicationId);
            _series.DeleteForApplication(applicationId);
            _episodes.DeleteForApplication(applicationId);
            _episodeFiles.DeleteForApplication(applicationId);
            _movies.DeleteForApplication(applicationId);
            _movieFiles.DeleteForApplication(applicationId);
            _artists.DeleteForApplication(applicationId);
            _albums.DeleteForApplication(applicationId);
            _trackFiles.DeleteForApplication(applicationId);

            var status = _syncStatus.ForApplication(applicationId);

            if (status != null)
            {
                _syncStatus.Delete(status);
            }
        }
    }
}

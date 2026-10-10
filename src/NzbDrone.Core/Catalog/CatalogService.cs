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
        public int MissingFiles { get; set; }
        public DateTime? LastSyncStarted { get; set; }
        public DateTime? LastSyncCompleted { get; set; }
        public string LastSyncError { get; set; }
    }

    public interface ICatalogService
    {
        void Refresh(int? applicationId, bool purgeMissing);
        List<CatalogApplicationSummary> GetSummaries();
        PagingSpec<CatalogEpisodeFile> PagedEpisodeFiles(PagingSpec<CatalogEpisodeFile> pagingSpec);
        PagingSpec<CatalogMovieFile> PagedMovieFiles(PagingSpec<CatalogMovieFile> pagingSpec);
        List<CatalogSeries> GetSeries(int applicationId, IEnumerable<int> arrSeriesIds);
        List<CatalogEpisode> GetEpisodesForFiles(int applicationId, IEnumerable<int> arrEpisodeFileIds);
        List<CatalogMovie> GetMovies(int applicationId, IEnumerable<int> arrMovieIds);
    }

    public class CatalogService : ICatalogService,
                                  IExecute<CatalogRefreshCommand>,
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

        public void Handle(ProviderDeletedEvent<IApplication> message)
        {
            DeleteForApplication(message.ProviderId);
        }

        public List<CatalogApplicationSummary> GetSummaries()
        {
            return _applicationFactory.All().Select(definition =>
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
                    MissingFiles = _episodeFiles.Count(definition.Id, true) + _movieFiles.Count(definition.Id, true),
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

        public PagingSpec<CatalogMovieFile> PagedMovieFiles(PagingSpec<CatalogMovieFile> pagingSpec)
        {
            return _movieFiles.GetPaged(pagingSpec);
        }

        public List<CatalogSeries> GetSeries(int applicationId, IEnumerable<int> arrSeriesIds)
        {
            return _series.ForArrIds(applicationId, arrSeriesIds);
        }

        public List<CatalogEpisode> GetEpisodesForFiles(int applicationId, IEnumerable<int> arrEpisodeFileIds)
        {
            return _episodes.ForFiles(applicationId, arrEpisodeFileIds);
        }

        public List<CatalogMovie> GetMovies(int applicationId, IEnumerable<int> arrMovieIds)
        {
            return _movies.ForArrIds(applicationId, arrMovieIds);
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

            status.LastSyncCompleted = now;
            status.LastSyncError = null;
            _syncStatus.Upsert(status);
            _eventAggregator.PublishEvent(new CatalogRefreshedEvent(applicationId));

            _logger.Info("Catalog for {0} refreshed: {1} series, {2} episodes, {3} episode files, {4} movies, {5} movie files",
                application.Definition.Name,
                catalog.Series.Count,
                catalog.Episodes.Count,
                catalog.EpisodeFiles.Count,
                catalog.Movies.Count,
                catalog.MovieFiles.Count);
        }

        // Inserts what is new, updates what changed, and marks what the Arr app stopped reporting.
        // Items missing since before purgeBefore are removed.
        private static void Merge<TModel>(ICatalogRepository<TModel> repository, int applicationId, List<TModel> incoming, DateTime now, DateTime purgeBefore)
            where TModel : CatalogModel, new()
        {
            var existing = repository.ForApplication(applicationId).ToDictionary(x => x.ArrId);
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

            var status = _syncStatus.ForApplication(applicationId);

            if (status != null)
            {
                _syncStatus.Delete(status);
            }
        }
    }
}

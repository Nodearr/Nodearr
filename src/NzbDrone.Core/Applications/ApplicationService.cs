using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Applications
{
    public class ApplicationService : IHandleAsync<ProviderAddedEvent<IIndexer>>,
                                      IHandleAsync<ProviderDeletedEvent<IIndexer>>,
                                      IHandleAsync<ProviderAddedEvent<IApplication>>,
                                      IHandleAsync<ProviderUpdatedEvent<IIndexer>>,
                                      IHandleAsync<ProviderUpdatedEvent<IApplication>>,
                                      IExecute<ApplicationIndexerSyncCommand>
    {
        private readonly IApplicationFactory _applicationsFactory;
        private readonly IAppIndexerMapService _appIndexerMapService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IApplicationStatusService _applicationStatusService;
        private readonly Logger _logger;

        public ApplicationService(IApplicationFactory applicationsFactory,
                                  IApplicationStatusService applicationStatusService,
                                  IAppIndexerMapService appIndexerMapService,
                                  IIndexerFactory indexerFactory,
                                  Logger logger)
        {
            _applicationsFactory = applicationsFactory;
            _applicationStatusService = applicationStatusService;
            _appIndexerMapService = appIndexerMapService;
            _indexerFactory = indexerFactory;
            _logger = logger;
        }

        public void HandleAsync(ProviderAddedEvent<IApplication> message)
        {
            var appDefinition = (ApplicationDefinition)message.Definition;

            if (appDefinition.Enable)
            {
                var app = _applicationsFactory.GetInstance(appDefinition);
                var indexers = GetEnabledIndexers();

                SyncIndexers(new List<IApplication> { app }, indexers);
            }
        }

        public void HandleAsync(ProviderUpdatedEvent<IApplication> message)
        {
            var appDefinition = (ApplicationDefinition)message.Definition;

            if (appDefinition.Enable)
            {
                var app = _applicationsFactory.GetInstance(appDefinition);
                var indexers = GetEnabledIndexers();

                SyncIndexers(new List<IApplication> { app }, indexers);
            }
        }

        public void HandleAsync(ProviderAddedEvent<IIndexer> message)
        {
            var enabledApps = _applicationsFactory.SyncEnabled();
            var indexer = PrepareIndexer((IndexerDefinition)message.Definition);

            foreach (var app in enabledApps)
            {
                if (ShouldHandleIndexer(app.Definition, indexer))
                {
                    ExecuteAction(a => a.AddIndexer(indexer), app);
                }
            }
        }

        public void HandleAsync(ProviderDeletedEvent<IIndexer> message)
        {
            var enabledApps = _applicationsFactory.SyncEnabled();

            foreach (var app in enabledApps)
            {
                ExecuteAction(a => a.RemoveIndexer(message.ProviderId), app);
            }
        }

        public void HandleAsync(ProviderUpdatedEvent<IIndexer> message)
        {
            var enabledApps = _applicationsFactory.SyncEnabled()
                .Where(n => ((ApplicationDefinition)n.Definition).SyncLevel == ApplicationSyncLevel.FullSync)
                .ToList();
            var indexer = PrepareIndexer((IndexerDefinition)message.Definition);

            SyncIndexers(enabledApps, new List<IndexerDefinition> { indexer });
        }

        public void Execute(ApplicationIndexerSyncCommand message)
        {
            var enabledApps = _applicationsFactory.SyncEnabled();
            var indexers = GetAllIndexers();

            SyncIndexers(enabledApps, indexers, true, message.ForceSync);
        }

        private List<IndexerDefinition> GetEnabledIndexers()
        {
            return _indexerFactory.GetAvailableProviders()
                .Select(i => (IndexerDefinition)i.Definition)
                .ToList();
        }

        private List<IndexerDefinition> GetAllIndexers()
        {
            var indexers = _indexerFactory.All();

            foreach (var indexer in indexers)
            {
                _indexerFactory.SetProviderCharacteristics(indexer);
            }

            return indexers;
        }

        private IndexerDefinition PrepareIndexer(IndexerDefinition indexer)
        {
            _indexerFactory.SetProviderCharacteristics(indexer);

            return indexer;
        }

        private void SyncIndexers(List<IApplication> applications, List<IndexerDefinition> indexers, bool removeRemote = false, bool forceSync = false)
        {
            var sortedIndexers = indexers.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var app in applications)
            {
                var indexerMappings = _appIndexerMapService.GetMappingsForApp(app.Definition.Id);
                var remoteMappings = ExecuteAction(a => a.GetIndexerMappings(), app);

                if (remoteMappings == null)
                {
                    continue;
                }

                foreach (var mapping in remoteMappings)
                {
                    if (!indexerMappings.Any(m => (m.RemoteIndexerId > 0 && m.RemoteIndexerId == mapping.RemoteIndexerId) ||
                                                  (m.RemoteIndexerName.IsNotNullOrWhiteSpace() && m.RemoteIndexerName == mapping.RemoteIndexerName)))
                    {
                        var addMapping = new AppIndexerMap
                        {
                            AppId = app.Definition.Id,
                            RemoteIndexerId = mapping.RemoteIndexerId,
                            RemoteIndexerName = mapping.RemoteIndexerName,
                            IndexerId = mapping.IndexerId
                        };

                        _appIndexerMapService.Insert(addMapping);
                        indexerMappings.Add(addMapping);
                    }
                }

                foreach (var indexer in sortedIndexers)
                {
                    if (indexerMappings.Any(x => x.IndexerId == indexer.Id))
                    {
                        if (((ApplicationDefinition)app.Definition).SyncLevel == ApplicationSyncLevel.FullSync && ShouldHandleIndexer(app.Definition, indexer))
                        {
                            ExecuteAction(a => a.UpdateIndexer(indexer, forceSync), app);
                        }
                    }
                    else if (indexer.Enable && ShouldHandleIndexer(app.Definition, indexer))
                    {
                        ExecuteAction(a => a.AddIndexer(indexer), app);
                    }
                }

                if (removeRemote)
                {
                    RemoveUnmatchedRemoteIndexers(app, indexerMappings);
                }
            }
        }

        private void RemoveUnmatchedRemoteIndexers(IApplication app, List<AppIndexerMap> indexerMappings)
        {
            var allIndexers = GetAllIndexers();

            foreach (var mapping in indexerMappings)
            {
                var indexer = allIndexers.FirstOrDefault(x => x.Id == mapping.IndexerId);

                if (indexer == null)
                {
                    _logger.Warn("Indexer with the ID {0} was found within {1} but is no longer defined within Nodearr, this is being removed.", mapping.IndexerId, app.Name);
                    ExecuteAction(a => a.RemoveIndexer(mapping.IndexerId), app);
                }
                else if (((ApplicationDefinition)app.Definition).SyncLevel == ApplicationSyncLevel.FullSync && !ShouldHandleIndexer(app.Definition, indexer))
                {
                    _logger.Warn("Indexer with the ID {0} was found within {1} but is no longer handled by Nodearr, this is being removed.", mapping.IndexerId, app.Name);
                    ExecuteAction(a => a.RemoveIndexer(mapping.IndexerId), app);
                }
            }
        }

        private bool ShouldHandleIndexer(ProviderDefinition app, ProviderDefinition indexer)
        {
            if (!indexer.Settings.Validate().IsValid)
            {
                _logger.Debug("Indexer {0} [{1}] has invalid settings.", indexer.Name, indexer.Id);

                return false;
            }

            if (app.Tags.Empty())
            {
                _logger.Debug("No tags set to application {0}.", app.Name);

                return true;
            }

            var intersectingTags = app.Tags.Intersect(indexer.Tags).ToArray();

            if (intersectingTags.Any())
            {
                _logger.Debug("Application {0} and indexer {1} [{2}] have {3} intersecting matching tags.", app.Name, indexer.Name, indexer.Id, intersectingTags.Length);

                return true;
            }

            _logger.Debug("Application {0} does not have any intersecting matching tags with {1} [{2}]. Indexer will neither be synced to nor removed from the application.", app.Name, indexer.Name, indexer.Id);

            return false;
        }

        private void ExecuteAction(Action<IApplication> applicationAction, IApplication application)
        {
            try
            {
                applicationAction(application);
                _applicationStatusService.RecordSuccess(application.Definition.Id);
            }
            catch (WebException webException)
            {
                RecordWebException(webException, application);
            }
            catch (TooManyRequestsException ex)
            {
                var minimumBackOff = ex.RetryAfter != TimeSpan.Zero ? ex.RetryAfter : TimeSpan.FromHours(1);
                _applicationStatusService.RecordFailure(application.Definition.Id, minimumBackOff);

                _logger.Warn(ex, "API request limit reached for {0}", application.Name);
            }
            catch (HttpException ex)
            {
                _applicationStatusService.RecordFailure(application.Definition.Id);
                _logger.Warn(ex, "{0} {1}", application.Name, ex.Message);
            }
            catch (Exception ex)
            {
                _applicationStatusService.RecordFailure(application.Definition.Id);
                _logger.Error(ex, "An error occurred while talking to remote application.");
            }
        }

        private TResult ExecuteAction<TResult>(Func<IApplication, TResult> applicationAction, IApplication application)
        {
            try
            {
                var result = applicationAction(application);
                _applicationStatusService.RecordSuccess(application.Definition.Id);

                return result;
            }
            catch (WebException webException)
            {
                RecordWebException(webException, application);
            }
            catch (TooManyRequestsException ex)
            {
                var minimumBackOff = ex.RetryAfter != TimeSpan.Zero ? ex.RetryAfter : TimeSpan.FromHours(1);
                _applicationStatusService.RecordFailure(application.Definition.Id, minimumBackOff);

                _logger.Warn(ex, "API request limit reached for {0}", application.Name);
            }
            catch (HttpException ex)
            {
                _applicationStatusService.RecordFailure(application.Definition.Id);
                _logger.Warn(ex, "{0} {1}", application.Name, ex.Message);
            }
            catch (Exception ex)
            {
                _applicationStatusService.RecordFailure(application.Definition.Id);
                _logger.Error(ex, "An error occurred while talking to remote application.");
            }

            return default(TResult);
        }

        private void RecordWebException(WebException webException, IApplication application)
        {
            if (webException.Status == WebExceptionStatus.NameResolutionFailure ||
                webException.Status == WebExceptionStatus.ConnectFailure)
            {
                _applicationStatusService.RecordConnectionFailure(application.Definition.Id);
            }
            else
            {
                _applicationStatusService.RecordFailure(application.Definition.Id);
            }

            if (webException.Message.Contains("502") ||
                webException.Message.Contains("503") ||
                webException.Message.Contains("timed out"))
            {
                _logger.Warn(webException, "{0} server is currently unavailable. {1}", application.Name, webException.Message);
            }
            else
            {
                _logger.Warn(webException, "{0} {1}", application.Name, webException.Message);
            }
        }
    }
}

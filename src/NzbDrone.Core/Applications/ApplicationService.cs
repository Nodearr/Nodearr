using System;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Applications
{
    public class ApplicationService : IHandleAsync<ProviderAddedEvent<IApplication>>,
                                      IHandleAsync<ProviderUpdatedEvent<IApplication>>,
                                      IExecute<ApplicationIndexerSyncCommand>
    {
        private readonly IApplicationFactory _applicationsFactory;
        private readonly IApplicationStatusService _applicationStatusService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ApplicationService(IApplicationFactory applicationsFactory,
                                  IApplicationStatusService applicationStatusService,
                                  IManageCommandQueue commandQueueManager,
                                  Logger logger)
        {
            _applicationsFactory = applicationsFactory;
            _applicationStatusService = applicationStatusService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void HandleAsync(ProviderAddedEvent<IApplication> message)
        {
            OnSaved((ApplicationDefinition)message.Definition);
        }

        public void HandleAsync(ProviderUpdatedEvent<IApplication> message)
        {
            OnSaved((ApplicationDefinition)message.Definition);
        }

        public void Execute(ApplicationIndexerSyncCommand message)
        {
            foreach (var application in _applicationsFactory.SyncEnabled())
            {
                SyncRegistration(application, message.ForceSync);
            }
        }

        private void OnSaved(ApplicationDefinition definition)
        {
            if (!definition.Enable)
            {
                return;
            }

            SyncRegistration(_applicationsFactory.GetInstance(definition), false);
            _commandQueueManager.Push(new CatalogRefreshCommand { ApplicationId = definition.Id });
        }

        private void SyncRegistration(IApplication application, bool force)
        {
            try
            {
                application.SyncRegistration(force);
                _applicationStatusService.RecordSuccess(application.Definition.Id);
            }
            catch (TooManyRequestsException ex)
            {
                var minimumBackOff = ex.RetryAfter != TimeSpan.Zero ? ex.RetryAfter : TimeSpan.FromHours(1);

                _applicationStatusService.RecordFailure(application.Definition.Id, minimumBackOff);
                _logger.Warn(ex, "API request limit reached for {0}", application.Definition.Name);
            }
            catch (ApplicationSyncException ex)
            {
                _applicationStatusService.RecordFailure(application.Definition.Id);
                _logger.Warn(ex, ex.Message);
            }
            catch (Exception ex)
            {
                _applicationStatusService.RecordFailure(application.Definition.Id);
                _logger.Error(ex, "Unable to register Nodearr with {0}", application.Definition.Name);
            }
        }
    }
}

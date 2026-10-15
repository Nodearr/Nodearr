using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Applications
{
    public interface IApplicationFactory : IProviderFactory<IApplication, ApplicationDefinition>
    {
        List<IApplication> SyncEnabled(bool filterBlockedApplications = true);
    }

    public class ApplicationFactory : ProviderFactory<IApplication, ApplicationDefinition>, IApplicationFactory
    {
        // How long deleting an application waits for the Arr app to answer. An app that is being
        // removed is often one that is no longer there.
        private static readonly TimeSpan RemovalWait = TimeSpan.FromSeconds(15);

        private readonly IApplicationStatusService _applicationStatusService;
        private readonly Logger _logger;

        public ApplicationFactory(IApplicationStatusService applicationStatusService,
                                  IApplicationsRepository providerRepository,
                                  IEnumerable<IApplication> providers,
                                  IServiceProvider container,
                                  IEventAggregator eventAggregator,
                                  Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _applicationStatusService = applicationStatusService;
            _logger = logger;
        }

        public List<IApplication> SyncEnabled(bool filterBlockedApplications = true)
        {
            var enabledApplications = GetAvailableProviders().Where(n => ((ApplicationDefinition)n.Definition).Enable);

            if (filterBlockedApplications)
            {
                return FilterBlockedApplications(enabledApplications).ToList();
            }

            return enabledApplications.ToList();
        }

        public override void Delete(int id)
        {
            RemoveFromApp(Find(id), new[] { id });
            base.Delete(id);
        }

        public override void Delete(IEnumerable<int> ids)
        {
            var leaving = ids.ToList();

            foreach (var id in leaving)
            {
                RemoveFromApp(Find(id), leaving);
            }

            base.Delete(leaving);
        }

        // An application that is deleted here would otherwise leave an indexer and a download client
        // behind in the Arr app, both pointing at a Nodearr that no longer answers for it. Removing
        // them is tried once. If the Arr app cannot be reached the application is deleted all the same.
        private void RemoveFromApp(ApplicationDefinition definition, ICollection<int> leaving)
        {
            if (definition == null)
            {
                return;
            }

            // Two applications set up for the one Arr app share what Nodearr added to it.
            if (All().Any(other => !leaving.Contains(other.Id) && SameApp(other, definition)))
            {
                _logger.Info("Another application still uses {0}, so Nodearr stays registered in it", definition.Name);

                return;
            }

            var application = GetInstance(definition);

            var removal = Task.Run(() =>
            {
                try
                {
                    application.RemoveRegistration();
                    _logger.Info("Removed Nodearr from {0}", definition.Name);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to remove Nodearr from {0}. If it still lists an indexer and a download client named Nodearr, delete them there.", definition.Name);
                }
            });

            if (!removal.Wait(RemovalWait))
            {
                _logger.Warn("{0} is slow to answer. The application is deleted, and removing Nodearr from it carries on in the background.", definition.Name);
            }
        }

        private static bool SameApp(ApplicationDefinition one, ApplicationDefinition other)
        {
            return one.Implementation == other.Implementation &&
                   one.Settings is ArrApplicationSettings a && other.Settings is ArrApplicationSettings b &&
                   string.Equals(a.BaseUrl?.TrimEnd('/'), b.BaseUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        protected override List<ApplicationDefinition> Active()
        {
            return base.Active().Where(c => c.Enable).ToList();
        }

        public override ValidationResult Test(ApplicationDefinition definition)
        {
            var result = base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                _applicationStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                _applicationStatusService.RecordFailure(definition.Id);
            }

            return result;
        }

        private IEnumerable<IApplication> FilterBlockedApplications(IEnumerable<IApplication> applications)
        {
            var blockedApplications = _applicationStatusService.GetBlockedProviders().ToDictionary(v => v.ProviderId, v => v);

            foreach (var application in applications)
            {
                if (blockedApplications.TryGetValue(application.Definition.Id, out var blockedApplicationStatus) && blockedApplicationStatus.DisabledTill.HasValue)
                {
                    _logger.Debug("Temporarily ignoring application {0} till {1} due to recent failures.", application.Definition.Name, blockedApplicationStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                yield return application;
            }
        }
    }
}

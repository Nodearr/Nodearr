using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Localization;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Nodearr puts a finished transfer in its download folder, and Sonarr or Radarr imports it
    // from there. If the app cannot see that folder, every transfer succeeds and no import ever
    // happens. This puts a marker in the folder and asks each app whether it can see it.
    [CheckOn(typeof(ProviderAddedEvent<IApplication>))]
    [CheckOn(typeof(ProviderUpdatedEvent<IApplication>))]
    [CheckOn(typeof(ProviderDeletedEvent<IApplication>))]
    public class ApplicationDownloadFolderCheck : HealthCheckBase
    {
        private readonly IApplicationFactory _applicationFactory;
        private readonly ITransferFolders _folders;
        private readonly Logger _logger;

        public ApplicationDownloadFolderCheck(IApplicationFactory applicationFactory, ITransferFolders folders, ILocalizationService localizationService, Logger logger)
            : base(localizationService)
        {
            _applicationFactory = applicationFactory;
            _folders = folders;
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            var applications = _applicationFactory.GetAvailableProviders()
                .Where(a => ((ApplicationDefinition)a.Definition).HoldsLibrary)
                .ToList();

            if (applications.Count == 0)
            {
                return new HealthCheck(GetType());
            }

            var marker = "nodearr-check-" + Guid.NewGuid().ToString("N")[..8];
            var markerPath = Path.Combine(_folders.Complete, marker);

            try
            {
                Directory.CreateDirectory(markerPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Error,
                    HealthCheckReason.DownloadFolderNotWritable,
                    _localizationService.GetLocalizedString("DownloadFolderNotWritableMessage",
                        new Dictionary<string, object> { { "path", _folders.Complete }, { "error", ex.Message } }));
            }

            try
            {
                foreach (var application in applications)
                {
                    FolderVisibility seen;

                    try
                    {
                        seen = application.LookForFolder(_folders.Complete, marker);
                    }
                    catch (Exception ex)
                    {
                        // The app could not be asked. That is a different problem with its own messages.
                        _logger.Debug(ex, "Unable to ask {0} about the download folder", application.Definition.Name);

                        continue;
                    }

                    if (!seen.Found)
                    {
                        return new HealthCheck(GetType(),
                            HealthCheckResult.Warning,
                            HealthCheckReason.DownloadFolderNotShared,
                            _localizationService.GetLocalizedString("ApplicationDownloadFolderCheckMessage",
                                new Dictionary<string, object>
                                {
                                    { "applicationName", application.Definition.Name },
                                    { "path", _folders.Complete },
                                    { "lookedAt", seen.LookedAt }
                                }));
                    }
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(markerPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Debug(ex, "Unable to remove {0}", markerPath);
                }
            }

            return new HealthCheck(GetType());
        }
    }
}

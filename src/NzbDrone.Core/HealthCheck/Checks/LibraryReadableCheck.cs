using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Localization;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // A file the catalog lists and Nodearr cannot open is a file another node will be offered and
    // then refused. The owner would never see that. The other person would.
    [CheckOn(typeof(CatalogRefreshedEvent))]
    [CheckOn(typeof(ModelEvent<RemotePathMapping>))]
    [CheckOn(typeof(ProviderDeletedEvent<IApplication>))]
    public class LibraryReadableCheck : HealthCheckBase
    {
        // Enough to tell a missing mount from a few moved files, without touching every file in
        // a large library each time.
        private const int SampleSize = 100;

        private readonly IApplicationFactory _applicationFactory;
        private readonly ICatalogEpisodeFileRepository _episodeFiles;
        private readonly ICatalogMovieFileRepository _movieFiles;
        private readonly ICatalogFileLocator _locator;

        public LibraryReadableCheck(IApplicationFactory applicationFactory,
                                    ICatalogEpisodeFileRepository episodeFiles,
                                    ICatalogMovieFileRepository movieFiles,
                                    ICatalogFileLocator locator,
                                    ILocalizationService localizationService)
            : base(localizationService)
        {
            _applicationFactory = applicationFactory;
            _episodeFiles = episodeFiles;
            _movieFiles = movieFiles;
            _locator = locator;
        }

        public override HealthCheck Check()
        {
            foreach (var application in _applicationFactory.All())
            {
                var files = _episodeFiles.ForApplication(application.Id).Cast<CatalogFile>()
                    .Concat(_movieFiles.ForApplication(application.Id))
                    .Where(f => f.MissingSince == null)
                    .ToList();

                var sample = Sample(files).Select(f => _locator.Locate(application.Id, f.Path)).ToList();

                if (sample.Count == 0)
                {
                    continue;
                }

                var states = sample.ToLookup(_locator.Open);
                var missing = states[CatalogFileAccess.Missing].ToList();
                var unreadable = states[CatalogFileAccess.NotAllowed].ToList();

                // Nothing found at all is a library that is not where Nodearr looks.
                if (missing.Count == sample.Count)
                {
                    return Warning(application, HealthCheckReason.LibraryNotFound, "LibraryNotFoundCheckMessage", missing, sample.Count);
                }

                // Files there that Nodearr may not open are down to the user it runs as.
                if (unreadable.Count > 0)
                {
                    return Warning(application, HealthCheckReason.LibraryNotReadable, "LibraryReadableCheckMessage", unreadable, sample.Count);
                }

                // A few missing among many that are there were moved, renamed or deleted after the
                // Arr app last looked.
                if (missing.Count > 0)
                {
                    return Warning(application, HealthCheckReason.LibraryFilesMissing, "LibraryFilesMissingCheckMessage", missing, sample.Count);
                }
            }

            return new HealthCheck(GetType());
        }

        private HealthCheck Warning(ApplicationDefinition application, HealthCheckReason reason, string message, List<LocatedFile> files, int checkedCount)
        {
            return new HealthCheck(GetType(),
                HealthCheckResult.Warning,
                reason,
                _localizationService.GetLocalizedString(message,
                    new Dictionary<string, object>
                    {
                        { "applicationName", application.Name },
                        { "count", files.Count },
                        { "checked", checkedCount },
                        { "path", files[0].LocalPath }
                    }));
        }

        // Spread evenly through the list, so that one bad folder among many is still likely found.
        private static List<CatalogFile> Sample(List<CatalogFile> files)
        {
            if (files.Count <= SampleSize)
            {
                return files;
            }

            var step = files.Count / (double)SampleSize;

            return Enumerable.Range(0, SampleSize).Select(i => files[(int)(i * step)]).ToList();
        }
    }
}

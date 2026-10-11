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
    // A file the catalog lists and Nodearr cannot read is a file another node will be offered
    // and then refused. The owner would never see that. The other person would.
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

                var sample = Sample(files);
                var unreadable = sample.Where(f => !_locator.Locate(application.Id, f.Path).Readable).ToList();

                if (unreadable.Count == 0)
                {
                    continue;
                }

                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.LibraryNotReadable,
                    _localizationService.GetLocalizedString("LibraryReadableCheckMessage",
                        new Dictionary<string, object>
                        {
                            { "applicationName", application.Name },
                            { "unreadable", unreadable.Count },
                            { "checked", sample.Count },
                            { "path", _locator.Locate(application.Id, unreadable[0].Path).LocalPath }
                        }));
            }

            return new HealthCheck(GetType());
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

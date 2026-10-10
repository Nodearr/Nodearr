using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Sonarr
{
    public class Sonarr : ApplicationBase<SonarrSettings>
    {
        public Sonarr(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger)
            : base(proxy, configFileProvider, logger)
        {
        }

        public override string Name => "Sonarr";
        protected override Version MinimumVersion => new(3, 0, 5, 0);
        protected override string CategoryField => "tvCategory";

        public override ApplicationCatalog FetchCatalog()
        {
            var catalog = new ApplicationCatalog();

            foreach (var profile in _proxy.Get<List<ArrQualityProfileResource>>(Settings, "/qualityprofile"))
            {
                catalog.QualityProfiles.Add(profile.ToCatalog());
            }

            // Sonarr has no bulk endpoint for episodes or episode files, so this is two requests per series.
            foreach (var series in _proxy.Get<List<SonarrSeriesResource>>(Settings, "/series"))
            {
                catalog.Series.Add(series.ToCatalog());

                foreach (var episode in _proxy.Get<List<SonarrEpisodeResource>>(Settings, $"/episode?seriesId={series.Id}"))
                {
                    catalog.Episodes.Add(episode.ToCatalog());
                }

                if (series.Statistics == null || series.Statistics.EpisodeFileCount > 0)
                {
                    foreach (var file in _proxy.Get<List<SonarrEpisodeFileResource>>(Settings, $"/episodefile?seriesId={series.Id}"))
                    {
                        catalog.EpisodeFiles.Add(file.ToCatalog());
                    }
                }
            }

            return catalog;
        }
    }
}

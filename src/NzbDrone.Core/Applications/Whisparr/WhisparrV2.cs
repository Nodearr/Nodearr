using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Whisparr
{
    // Whisparr v2, built on Sonarr: its API, download client and webhook are Sonarr's, a site is a
    // series and a scene is an episode. Nodearr keeps each scene as Whisparr v3 keeps one, a movie
    // marked as an adult scene with its site as the studio, so either version finds the other's
    // scenes by Whisparr's text search (ADR 0015, amendments).
    public class WhisparrV2 : ApplicationBase<WhisparrV2Settings>, IPosterSource
    {
        public WhisparrV2(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger)
            : base(proxy, configFileProvider, logger)
        {
        }

        public override string Name => "Whisparr v2";
        protected override string ReportedAppName => "Whisparr";
        protected override Version MinimumVersion => new(2, 0, 0, 0);

        // Whisparr v3 calls itself Whisparr too, and is added as Whisparr v3.
        protected override Version BelowVersion => new(3, 0, 0, 0);
        protected override string CategoryField => "tvCategory";

        protected override IReadOnlyDictionary<string, bool> WebhookEvents => new Dictionary<string, bool>
        {
            { "onGrab", false },
            { "onDownload", true },
            { "onUpgrade", true },
            { "onImportComplete", false },
            { "onRename", true },
            { "onSeriesAdd", true },
            { "onSeriesDelete", true },
            { "onEpisodeFileDelete", true },
            { "onEpisodeFileDeleteForUpgrade", true },
            { "onHealthIssue", false },
            { "onHealthRestored", false },
            { "onApplicationUpdate", false },
            { "onManualInteractionRequired", false }
        };

        public override ApplicationCatalog FetchCatalog()
        {
            var catalog = new ApplicationCatalog();

            foreach (var profile in _proxy.Get<List<ArrQualityProfileResource>>(Settings, "/qualityprofile"))
            {
                catalog.QualityProfiles.Add(profile.ToCatalog());
            }

            // One request per site, for its scenes with their files.
            foreach (var site in _proxy.Get<List<WhisparrV2SiteResource>>(Settings, "/series"))
            {
                foreach (var scene in _proxy.Get<List<WhisparrV2SceneResource>>(Settings, $"/episode?seriesId={site.Id}&includeEpisodeFile=true"))
                {
                    catalog.Movies.Add(scene.ToCatalog(site));

                    if (scene.EpisodeFile != null && scene.EpisodeFile.Id > 0)
                    {
                        scene.EpisodeFile.MovieId = scene.Id;
                        catalog.MovieFiles.Add(scene.EpisodeFile.ToCatalog());
                    }
                }
            }

            return catalog;
        }

        // The webhook names a site. The catalog is read again whole instead (RefreshesWhole).
        public override ApplicationCatalog FetchTitle(int arrId)
        {
            return FetchCatalog();
        }

        // A scene's picture is its site's poster: Whisparr v2 keeps none for the scene itself.
        public byte[] GetPoster(PosterSubject subject, int arrId)
        {
            if (subject != PosterSubject.Movie)
            {
                return null;
            }

            var scene = GetOrNull<JObject>($"/episode/{arrId}");

            return scene == null ? null : PosterImages.FromMediaCover(_proxy, Settings, $"{scene.Value<int>("seriesId")}/poster-500.jpg");
        }
    }
}

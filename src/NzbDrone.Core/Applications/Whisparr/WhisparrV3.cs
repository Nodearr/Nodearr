using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Whisparr
{
    // Whisparr v3, also called Eros. It is built on Radarr, so its API, its download client and its
    // webhook are Radarr's. A film or a scene is a movie with one file, and Nodearr keeps them as
    // movies marked as adult content, which only an adult search finds (ADR 0015, amendments).
    public class WhisparrV3 : ApplicationBase<WhisparrV3Settings>
    {
        public WhisparrV3(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger)
            : base(proxy, configFileProvider, logger)
        {
        }

        public override string Name => "Whisparr v3";
        protected override string ReportedAppName => "Whisparr";

        // v3 is Eros. Whisparr v2, built on Sonarr, reports a lower version and is turned away here.
        protected override Version MinimumVersion => new(3, 0, 0, 0);
        protected override string CategoryField => "movieCategory";

        protected override IReadOnlyDictionary<string, bool> WebhookEvents => new Dictionary<string, bool>
        {
            { "onGrab", false },
            { "onDownload", true },
            { "onUpgrade", true },
            { "onRename", true },
            { "onMovieAdded", true },
            { "onMovieDelete", true },
            { "onMovieFileDelete", true },
            { "onMovieFileDeleteForUpgrade", true },
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

            // Each film's or scene's file comes inline, as Radarr's do.
            foreach (var movie in _proxy.Get<List<WhisparrMovieResource>>(Settings, "/movie"))
            {
                Add(catalog, movie);
            }

            return catalog;
        }

        public override ApplicationCatalog FetchTitle(int arrId)
        {
            var catalog = new ApplicationCatalog();
            var movie = GetOrNull<WhisparrMovieResource>($"/movie/{arrId}");

            if (movie != null)
            {
                Add(catalog, movie);
            }

            return catalog;
        }

        private static void Add(ApplicationCatalog catalog, WhisparrMovieResource movie)
        {
            catalog.Movies.Add(movie.ToCatalog());

            if (movie.MovieFile != null)
            {
                movie.MovieFile.MovieId = movie.Id;
                catalog.MovieFiles.Add(movie.MovieFile.ToCatalog());
            }
        }
    }
}

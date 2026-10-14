using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Radarr
{
    public class Radarr : ApplicationBase<RadarrSettings>
    {
        public Radarr(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger)
            : base(proxy, configFileProvider, logger)
        {
        }

        public override string Name => "Radarr";
        protected override Version MinimumVersion => new(3, 0, 0, 0);
        protected override string CategoryField => "movieCategory";

        // Anything that adds, changes or removes a file or a movie. Grabs, health and updates are
        // no concern of Nodearr's.
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

            // Radarr returns each movie's file inline.
            foreach (var movie in _proxy.Get<List<RadarrMovieResource>>(Settings, "/movie"))
            {
                catalog.Movies.Add(movie.ToCatalog());

                if (movie.MovieFile != null)
                {
                    movie.MovieFile.MovieId = movie.Id;
                    catalog.MovieFiles.Add(movie.MovieFile.ToCatalog());
                }
            }

            return catalog;
        }

        public override ApplicationCatalog FetchTitle(int arrId)
        {
            var catalog = new ApplicationCatalog();
            var movie = GetOrNull<RadarrMovieResource>($"/movie/{arrId}");

            if (movie == null)
            {
                return catalog;
            }

            catalog.Movies.Add(movie.ToCatalog());

            if (movie.MovieFile != null)
            {
                movie.MovieFile.MovieId = movie.Id;
                catalog.MovieFiles.Add(movie.MovieFile.ToCatalog());
            }

            return catalog;
        }
    }
}

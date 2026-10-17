using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Radarr
{
    public class Radarr : ApplicationBase<RadarrSettings>, ILibraryAdder, IPosterSource
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

        public LibraryAddChoices GetAddChoices()
        {
            return LibraryAdd.Choices(_proxy, Settings, false);
        }

        public string AddAndSearch(LibraryAddRequest request)
        {
            return LibraryAdd.AddMovie(_proxy, Settings, Name, request, () => Lookup(request), GetOrNull<JObject>);
        }

        private JObject Lookup(LibraryAddRequest request)
        {
            if (request.TmdbId > 0)
            {
                return GetOrNull<JObject>($"/movie/lookup/tmdb?tmdbId={request.TmdbId}");
            }

            return string.IsNullOrWhiteSpace(request.ImdbId) ? null : GetOrNull<JObject>($"/movie/lookup/imdb?imdbId={request.ImdbId}");
        }

        public byte[] GetPoster(PosterSubject subject, int arrId)
        {
            return subject == PosterSubject.Movie ? PosterImages.FromMediaCover(_proxy, Settings, $"{arrId}/poster-500.jpg") : null;
        }
    }
}

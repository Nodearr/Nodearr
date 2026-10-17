using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Radarr
{
    public class Radarr : ApplicationBase<RadarrSettings>, ILibraryAdder
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
            var movie = request.ExistingArrId.HasValue ? GetOrNull<JObject>($"/movie/{request.ExistingArrId}") : null;
            var added = false;

            if (movie == null)
            {
                var found = Lookup(request) ?? throw new LibraryAddException("Radarr could not find it on TMDb");

                if (found.Value<int?>("id") is > 0)
                {
                    movie = _proxy.Get<JObject>(Settings, $"/movie/{found.Value<int>("id")}");
                }
                else
                {
                    LibraryAdd.CheckOptions(GetAddChoices(), request, Name);

                    found["qualityProfileId"] = request.QualityProfileId;
                    found["rootFolderPath"] = request.RootFolderPath;
                    found["monitored"] = true;
                    found["minimumAvailability"] = "released";
                    found["addOptions"] = new JObject { ["monitor"] = "movieOnly", ["searchForMovie"] = true };

                    _proxy.Post(Settings, "/movie", found);
                    added = true;
                }
            }

            if (!added)
            {
                movie["monitored"] = true;
                _proxy.Put(Settings, $"/movie/{movie.Value<int>("id")}", movie);
                _proxy.Post(Settings, "/command", new JObject { ["name"] = "MoviesSearch", ["movieIds"] = new JArray(movie.Value<int>("id")) });
            }

            return $"{(added ? "Added to" : "Already in")} Radarr, monitored, and searching";
        }

        private JObject Lookup(LibraryAddRequest request)
        {
            if (request.TmdbId > 0)
            {
                return GetOrNull<JObject>($"/movie/lookup/tmdb?tmdbId={request.TmdbId}");
            }

            return string.IsNullOrWhiteSpace(request.ImdbId) ? null : GetOrNull<JObject>($"/movie/lookup/imdb?imdbId={request.ImdbId}");
        }
    }
}

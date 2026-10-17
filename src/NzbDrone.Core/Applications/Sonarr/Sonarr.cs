using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Sonarr
{
    public class Sonarr : ApplicationBase<SonarrSettings>, ILibraryAdder, IPosterSource
    {
        public Sonarr(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger)
            : base(proxy, configFileProvider, logger)
        {
        }

        public override string Name => "Sonarr";
        protected override Version MinimumVersion => new(3, 0, 5, 0);
        protected override string CategoryField => "tvCategory";

        // Anything that adds, changes or removes a file or a series. Grabs, health and updates are
        // no concern of Nodearr's. Import Complete would repeat what each file's import says.
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

        public override ApplicationCatalog FetchTitle(int arrId)
        {
            var catalog = new ApplicationCatalog();
            var series = GetOrNull<SonarrSeriesResource>($"/series/{arrId}");

            if (series == null)
            {
                return catalog;
            }

            catalog.Series.Add(series.ToCatalog());

            foreach (var episode in _proxy.Get<List<SonarrEpisodeResource>>(Settings, $"/episode?seriesId={arrId}"))
            {
                catalog.Episodes.Add(episode.ToCatalog());
            }

            foreach (var file in _proxy.Get<List<SonarrEpisodeFileResource>>(Settings, $"/episodefile?seriesId={arrId}"))
            {
                catalog.EpisodeFiles.Add(file.ToCatalog());
            }

            return catalog;
        }

        public LibraryAddChoices GetAddChoices()
        {
            return LibraryAdd.Choices(_proxy, Settings, false);
        }

        // Seasons are monitored on the series, so episodes that arrive in them later are too. Single
        // episodes are monitored once Sonarr has read them. Then each part is searched for, as
        // Sonarr's own Search Monitored would.
        public string AddAndSearch(LibraryAddRequest request)
        {
            var seasons = request.Seasons.ToHashSet();
            var series = request.ExistingArrId.HasValue ? GetOrNull<JObject>($"/series/{request.ExistingArrId}") : null;
            var added = false;

            if (series == null)
            {
                var found = _proxy.GetArray(Settings, $"/series/lookup?term=tvdb:{request.TvdbId}").OfType<JObject>().FirstOrDefault()
                            ?? throw new LibraryAddException("Sonarr could not find it on TheTVDB");

                if (found.Value<int?>("id") is > 0)
                {
                    series = _proxy.Get<JObject>(Settings, $"/series/{found.Value<int>("id")}");
                }
                else
                {
                    LibraryAdd.CheckOptions(GetAddChoices(), request, Name);
                    series = _proxy.Post(Settings, "/series", ToAdd(found, request, seasons));
                    added = true;
                }
            }

            var id = series.Value<int>("id");

            if (!added)
            {
                series["monitored"] = true;

                foreach (var season in series["seasons"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    if (Wanted(season.Value<int>("seasonNumber"), request.Whole, seasons))
                    {
                        season["monitored"] = true;
                    }
                }

                if (request.Whole)
                {
                    series["monitorNewItems"] = "all";
                }

                // Sonarr monitors a season's episodes to match the season.
                _proxy.Put(Settings, $"/series/{id}", series);
            }

            // Until Sonarr has read the series' episodes there is nothing to monitor or search.
            var episodes = LibraryAdd.WaitFor(
                () => _proxy.GetArray(Settings, $"/episode?seriesId={id}").OfType<JObject>().ToList(),
                list => list.Count > 0 &&
                        list.Where(e => Wanted(e.Value<int>("seasonNumber"), request.Whole, seasons)).All(e => e.Value<bool>("monitored")) &&
                        !(added && LibraryAdd.IsRunning(_proxy, Settings, "RefreshSeries")));

            var wantedEpisodes = request.Episodes
                .Select(n => episodes.FirstOrDefault(e => e.Value<int>("seasonNumber") == n.SeasonNumber && e.Value<int>("episodeNumber") == n.EpisodeNumber))
                .Where(e => e != null)
                .Select(e => e.Value<int>("id"))
                .Distinct()
                .ToList();

            if (wantedEpisodes.Count > 0)
            {
                _proxy.PutMany(Settings, "/episode/monitor", new JObject { ["episodeIds"] = new JArray(wantedEpisodes), ["monitored"] = true });
            }

            // Each episode sent that Sonarr does not have is searched for by itself. A Season Search
            // takes only season packs for a season that ended long ago (Sonarr's Season Search
            // Maximum Single Episode Age), and a friend offers episode files one by one.
            var search = episodes
                .Where(e => (Wanted(e.Value<int>("seasonNumber"), request.Whole, seasons) && e.Value<bool>("monitored")) || wantedEpisodes.Contains(e.Value<int>("id")))
                .Where(e => !e.Value<bool>("hasFile"))
                .Select(e => e.Value<int>("id"))
                .Distinct()
                .ToList();

            if (search.Count > 0)
            {
                _proxy.Post(Settings, "/command", new JObject { ["name"] = "EpisodeSearch", ["episodeIds"] = new JArray(search) });
            }

            var parts = new List<string>();

            if (request.Whole)
            {
                parts.Add("the whole series");
            }
            else
            {
                if (seasons.Count > 0)
                {
                    parts.Add((seasons.Count == 1 ? "Season " : "Seasons ") + LibraryAdd.Join(seasons.OrderBy(n => n).Select(n => n.ToString()).ToList()));
                }

                if (wantedEpisodes.Count > 0)
                {
                    parts.Add(wantedEpisodes.Count == 1 ? "1 episode" : $"{wantedEpisodes.Count} episodes");
                }
            }

            var missing = request.Episodes.Count - wantedEpisodes.Count;
            var note = missing > 0 ? $". Sonarr does not list {missing} of the episodes sent" : string.Empty;

            var searching = search.Count == 0 ? "and has every episode of it already" : search.Count == 1 ? "and searching for 1 episode" : $"and searching for {search.Count} episodes";

            return $"{(added ? "Added to" : "Already in")} Sonarr, monitoring {(parts.Count > 0 ? LibraryAdd.Join(parts) : "nothing new")}, {searching}{note}";
        }

        private static bool Wanted(int seasonNumber, bool whole, HashSet<int> seasons)
        {
            // A whole series is what Sonarr calls All Episodes, which leaves specials out.
            return whole ? seasonNumber > 0 : seasons.Contains(seasonNumber);
        }

        private JObject ToAdd(JObject found, LibraryAddRequest request, HashSet<int> seasons)
        {
            found["qualityProfileId"] = request.QualityProfileId;
            found["rootFolderPath"] = request.RootFolderPath;
            found["seasonFolder"] = request.SeasonFolder;
            found["seriesType"] = request.SeriesType is "standard" or "daily" or "anime" ? request.SeriesType : "standard";
            found["monitored"] = true;
            found["monitorNewItems"] = request.Whole ? "all" : "none";

            // Sonarr 3 needs a language profile. Sonarr 4 has one left, which it ignores.
            if (found["languageProfileId"] == null || found.Value<int>("languageProfileId") == 0)
            {
                var language = LanguageProfile();

                if (language.HasValue)
                {
                    found["languageProfileId"] = language.Value;
                }
            }

            foreach (var season in found["seasons"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                season["monitored"] = Wanted(season.Value<int>("seasonNumber"), request.Whole, seasons);
            }

            // No monitor option: Sonarr monitors each season's episodes as the season is. Searching
            // waits until the parts are monitored.
            found["addOptions"] = new JObject
            {
                ["searchForMissingEpisodes"] = false,
                ["searchForCutoffUnmetEpisodes"] = false
            };

            return found;
        }

        private int? LanguageProfile()
        {
            try
            {
                return _proxy.GetArray(Settings, "/languageprofile").FirstOrDefault()?.Value<int>("id");
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Sonarr has no language profiles");

                return null;
            }
        }

        public byte[] GetPoster(PosterSubject subject, int arrId)
        {
            return subject == PosterSubject.Series ? PosterImages.FromMediaCover(_proxy, Settings, $"{arrId}/poster-500.jpg") : null;
        }
    }
}

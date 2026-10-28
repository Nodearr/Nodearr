using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.PeerCatalog;

namespace NzbDrone.Core.Sharing
{
    // The identity and parts to add, independent of whether the person chose a send or a catalog item.
    public class GetItTitle
    {
        public string Title { get; set; }
        public MediaKind MediaKind { get; set; }
        public bool IsAdult { get; set; }
        public bool IsWhole { get; set; }
        public int TvdbId { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string ForeignArtistId { get; set; }
        public string ForeignId { get; set; }
        public string StashId { get; set; }
        public string TpdbId { get; set; }
        public string SeriesType { get; set; }
        public string OfferIds { get; set; }
        public SentParts Parts { get; set; } = new();
        public List<PeerCatalogItem> Offers { get; set; } = new();

        public SentParts GetParts() => Parts;

        public static GetItTitle FromReceived(ReceivedTitle received, List<PeerCatalogItem> offers)
        {
            return new GetItTitle
            {
                Title = received.Title,
                MediaKind = received.MediaKind,
                IsAdult = received.IsAdult,
                IsWhole = received.IsWhole,
                TvdbId = received.TvdbId,
                TmdbId = received.TmdbId,
                ImdbId = received.ImdbId,
                ForeignArtistId = received.ForeignArtistId,
                ForeignId = received.ForeignId,
                StashId = received.StashId,
                TpdbId = received.TpdbId,
                SeriesType = received.SeriesType,
                OfferIds = received.OfferIds,
                Parts = received.GetParts(),
                Offers = offers
            };
        }
    }

    public interface ILibraryGetItService
    {
        GetItPlan Plan(GetItTitle title);
        ApplicationDefinition CheckOptions(GetItTitle title, GetItOptions options);
        string AddAndSearch(GetItTitle title, GetItOptions options);
    }

    // One add, monitor and search path for received sends and catalog selections.
    public class LibraryGetItService : ILibraryGetItService
    {
        private readonly IApplicationFactory _applicationFactory;
        private readonly ICatalogSeriesRepository _series;
        private readonly ICatalogMovieRepository _movies;
        private readonly ICatalogArtistRepository _artists;
        private readonly Logger _logger;

        public LibraryGetItService(IApplicationFactory applicationFactory,
                                   ICatalogSeriesRepository series,
                                   ICatalogMovieRepository movies,
                                   ICatalogArtistRepository artists,
                                   Logger logger)
        {
            _applicationFactory = applicationFactory;
            _series = series;
            _movies = movies;
            _artists = artists;
            _logger = logger;
        }

        public GetItPlan Plan(GetItTitle received)
        {
            var plan = new GetItPlan();
            var implementation = AppFor(received);

            if (implementation == null)
            {
                plan.Problem = "Nodearr cannot add this kind of title to an app";

                return plan;
            }

            var appName = implementation == nameof(Applications.Whisparr.WhisparrV3) ? "Whisparr v3" : implementation;
            var request = RequestFor(received);

            if (request == null)
            {
                plan.Problem = IsWhisparrV2Scene(received)
                    ? "This scene comes from Whisparr v2, which knows it by an ID Whisparr v3 cannot look up. Add it in Whisparr, and Whisparr finds it from your friend when it searches"
                    : $"Nodearr does not know what {appName} calls this yet. It will once your friend's catalog has arrived";

                return plan;
            }

            var definitions = _applicationFactory.All().Where(d => d.Implementation == implementation).ToList();

            if (definitions.Count == 0)
            {
                // Whisparr v2 is built on Sonarr, and a scene in it belongs to a site, which a friend's
                // catalog does not name by its ID there.
                plan.Problem = received.IsAdult && _applicationFactory.All().Any(d => d.Implementation == nameof(Applications.Whisparr.WhisparrV2))
                    ? "Nodearr cannot add to Whisparr v2 yet. Add it in Whisparr, and Whisparr finds it from your friend when it searches"
                    : $"Connect {appName} on Settings > Apps, and Nodearr can add this to it";

                return plan;
            }

            foreach (var definition in definitions)
            {
                var target = new GetItTarget
                {
                    ApplicationId = definition.Id,
                    ApplicationName = definition.Name,
                    Implementation = definition.Implementation,
                    InLibrary = ExistingArrId(received, request, definition.Id).HasValue
                };

                try
                {
                    target.Choices = Adder(definition).GetAddChoices();
                    target.Defaults = Defaults(received, definition.Id, target.Choices);
                }
                catch (Exception ex) when (ex is HttpException or System.Net.Http.HttpRequestException or Newtonsoft.Json.JsonException)
                {
                    _logger.Debug(ex, "Unable to ask {0} how to add {1}", definition.Name, received.Title);
                    target.Problem = $"{definition.Name} did not answer: {ex.Message}";
                }

                if (target.Problem == null && !target.InLibrary)
                {
                    if (target.Choices.RootFolders.Count == 0)
                    {
                        target.Problem = $"{definition.Name} has no root folder yet. Add one in {definition.Name}, then try again";
                    }
                    else if (target.Choices.QualityProfiles.Count == 0)
                    {
                        target.Problem = $"{definition.Name} has no quality profile yet. Add one in {definition.Name}, then try again";
                    }
                    else if (received.MediaKind == MediaKind.Music && target.Choices.MetadataProfiles.Count == 0)
                    {
                        target.Problem = $"{definition.Name} has no metadata profile yet. Add one in {definition.Name}, then try again";
                    }
                }

                plan.Targets.Add(target);
            }

            return plan;
        }

        public ApplicationDefinition CheckOptions(GetItTitle title, GetItOptions options)
        {
            var definition = _applicationFactory.Find(options.ApplicationId);

            if (definition == null || definition.Implementation != AppFor(title))
            {
                throw new GetItException("Choose an app this can be added to");
            }

            CheckOptionsForApp(title, definition, options);

            return definition;
        }

        public string AddAndSearch(GetItTitle title, GetItOptions options)
        {
            var definition = CheckOptions(title, options);
            var request = RequestFor(title) ?? throw new GetItException("Nodearr does not know what the app calls this yet");

            request.ExistingArrId = ExistingArrId(title, request, definition.Id);
            request.RootFolderPath = options.RootFolderPath;
            request.QualityProfileId = options.QualityProfileId;
            request.MetadataProfileId = options.MetadataProfileId;
            request.SeriesType = options.SeriesType ?? title.SeriesType;
            request.SeasonFolder = options.SeasonFolder;

            return Adder(definition).AddAndSearch(request);
        }

        // The app a title of this kind goes to. Adult content goes to Whisparr v3 only.
        private static string AppFor(GetItTitle received)
        {
            return received.MediaKind switch
            {
                MediaKind.Tv => "Sonarr",
                MediaKind.Movie => received.IsAdult ? nameof(Applications.Whisparr.WhisparrV3) : "Radarr",
                MediaKind.Music => "Lidarr",
                _ => null
            };
        }

        // What was sent, by IDs and parts. The note says, and the friend's catalog makes up for a
        // note that does not. Null while neither knows the title's IDs.
        private LibraryAddRequest RequestFor(GetItTitle received)
        {
            var parts = received.GetParts();
            var request = new LibraryAddRequest
            {
                TvdbId = received.TvdbId,
                TmdbId = received.TmdbId,
                ImdbId = received.ImdbId,
                ForeignArtistId = received.ForeignArtistId,
                StashId = received.StashId,
                TpdbId = received.TpdbId,
                Whole = received.IsWhole,
                Seasons = parts.Seasons,
                Episodes = parts.Episodes,
                Albums = parts.Albums
            };

            var offered = received.OfferIds?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet() ?? new HashSet<string>();
            var items = new Lazy<List<PeerCatalogItem>>(() => received.Offers.Where(i => offered.Contains(i.RemoteId)).ToList());
            var nothingSaid = !request.Whole && parts.Seasons.Count + parts.Episodes.Count + parts.Albums.Count == 0;

            switch (received.MediaKind)
            {
                case MediaKind.Tv:
                    if (request.TvdbId == 0)
                    {
                        request.TvdbId = items.Value.Select(i => i.TvdbId).FirstOrDefault(i => i > 0);
                    }

                    // Every episode of the series the friend offers now, which for a whole series may
                    // be more than when it was sent.
                    request.OfferedEpisodes = received.Offers
                        .Where(i => i.MediaKind == MediaKind.Tv && i.TvdbId > 0 && i.TvdbId == request.TvdbId)
                        .SelectMany(i => i.GetEpisodeNumbers().Select(e => new SeasonEpisode { SeasonNumber = i.SeasonNumber, EpisodeNumber = e }))
                        .ToList();

                    // Sonarr must search an offered upgrade even when it already has the episode.
                    // Its own profile still decides whether any result may replace that file.
                    request.UpgradeEpisodes = received.Offers
                        .Where(i => i.MediaKind == MediaKind.Tv && i.TvdbId == request.TvdbId && i.LocalStatus == PeerItemStatus.Upgrade)
                        .SelectMany(i => i.GetEpisodeNumbers().Select(e => new SeasonEpisode { SeasonNumber = i.SeasonNumber, EpisodeNumber = e }))
                        .ToList();

                    if (nothingSaid)
                    {
                        request.Episodes = items.Value
                            .SelectMany(i => (i.EpisodeNumbers ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(n => int.TryParse(n, out var e) ? new SeasonEpisode { SeasonNumber = i.SeasonNumber, EpisodeNumber = e } : null))
                            .Where(e => e != null)
                            .ToList();
                    }

                    return request.TvdbId > 0 ? request : null;

                case MediaKind.Movie:
                    if (request.TmdbId == 0 && request.ImdbId == null && request.StashId == null && request.TpdbId == null)
                    {
                        request.TmdbId = items.Value.Select(i => i.TmdbId).FirstOrDefault(i => i > 0);
                        request.ImdbId = items.Value.Select(i => i.ImdbId).FirstOrDefault(i => i != null);

                        if (received.IsAdult)
                        {
                            request.StashId = items.Value.Select(i => i.StashId).FirstOrDefault(i => i != null);
                            request.TpdbId = items.Value.Select(i => i.TpdbId).FirstOrDefault(i => i != null);
                        }
                    }

                    return request.TmdbId > 0 || request.ImdbId != null || request.StashId != null || request.TpdbId != null ? request : null;

                case MediaKind.Music:
                    if (request.ForeignArtistId == null)
                    {
                        request.ForeignArtistId = items.Value.Select(i => i.ForeignArtistId).FirstOrDefault(i => i != null);
                    }

                    if (nothingSaid)
                    {
                        request.Albums = items.Value.Select(i => i.ForeignAlbumId).Where(a => a != null).Distinct().ToList();
                    }

                    request.OfferedAlbums = received.Offers
                        .Where(i => i.ForeignArtistId != null && string.Equals(i.ForeignArtistId, request.ForeignArtistId, StringComparison.OrdinalIgnoreCase) && i.ForeignAlbumId != null)
                        .Select(i => i.ForeignAlbumId)
                        .Distinct()
                        .ToList();

                    request.UpgradeAlbums = received.Offers
                        .Where(i => i.MediaKind == MediaKind.Music && i.LocalStatus == PeerItemStatus.Upgrade &&
                            string.Equals(i.ForeignArtistId, request.ForeignArtistId, StringComparison.OrdinalIgnoreCase) && i.ForeignAlbumId != null)
                        .Select(i => i.ForeignAlbumId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    return request.ForeignArtistId != null ? request : null;

                default:
                    return null;
            }
        }

        private int? ExistingArrId(GetItTitle received, LibraryAddRequest request, int applicationId)
        {
            return received.MediaKind switch
            {
                MediaKind.Tv => _series.ForApplication(applicationId).FirstOrDefault(s => s.MissingSince == null && s.TvdbId == request.TvdbId)?.ArrId,
                MediaKind.Movie => _movies.ForApplication(applicationId).FirstOrDefault(m => m.MissingSince == null && IsSameMovie(m, received, request))?.ArrId,
                MediaKind.Music => _artists.ForApplication(applicationId).FirstOrDefault(a => a.MissingSince == null &&
                                                                                           string.Equals(a.ForeignArtistId, request.ForeignArtistId, StringComparison.OrdinalIgnoreCase))?.ArrId,
                _ => null
            };
        }

        // A film or a scene is the same one by any ID both know it by. Whisparr's own ID only
        // means the same thing between two Whisparrs.
        private static bool IsSameMovie(CatalogMovie movie, GetItTitle received, LibraryAddRequest request)
        {
            return (request.TmdbId > 0 && movie.TmdbId == request.TmdbId) ||
                   (request.ImdbId != null && movie.ImdbId == request.ImdbId) ||
                   (received.IsAdult && request.StashId != null && string.Equals(movie.StashId, request.StashId, StringComparison.OrdinalIgnoreCase)) ||
                   (received.IsAdult && request.TpdbId != null && string.Equals(movie.TpdbId, request.TpdbId, StringComparison.OrdinalIgnoreCase)) ||
                   (received.IsAdult && received.ForeignId != null && movie.ForeignId == received.ForeignId);
        }

        // A scene a friend's Whisparr v2 holds, known only by v2's own number for it.
        private bool IsWhisparrV2Scene(GetItTitle received)
        {
            if (!received.IsAdult)
            {
                return false;
            }

            if (received.ForeignId?.StartsWith("whisparr2:", StringComparison.Ordinal) == true)
            {
                return true;
            }

            var offered = received.OfferIds?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet() ?? new HashSet<string>();

            return received.Offers.Any(i => offered.Contains(i.RemoteId) && i.ForeignId?.StartsWith("whisparr2:", StringComparison.Ordinal) == true);
        }

        // The way most of the library already is.
        private GetItOptions Defaults(GetItTitle received, int applicationId, LibraryAddChoices choices)
        {
            var titles = received.MediaKind switch
            {
                MediaKind.Tv => _series.ForApplication(applicationId).Where(s => s.MissingSince == null).Select(s => (s.Path, s.QualityProfileId)).ToList(),
                MediaKind.Movie => _movies.ForApplication(applicationId).Where(m => m.MissingSince == null).Select(m => (m.Path, m.QualityProfileId)).ToList(),
                _ => _artists.ForApplication(applicationId).Where(a => a.MissingSince == null).Select(a => (a.Path, a.QualityProfileId)).ToList()
            };

            var root = choices.RootFolders
                .Select((f, i) => (f.Path, Index: i, Count: titles.Count(t => IsUnder(t.Path, f.Path))))
                .OrderByDescending(f => f.Count)
                .ThenBy(f => f.Index)
                .Select(f => f.Path)
                .FirstOrDefault();

            var profileIds = choices.QualityProfiles.Select(p => p.Id).ToHashSet();
            var profile = titles
                .Where(t => profileIds.Contains(t.QualityProfileId))
                .GroupBy(t => t.QualityProfileId)
                .OrderByDescending(g => g.Count())
                .Select(g => (int?)g.Key)
                .FirstOrDefault() ?? choices.QualityProfiles.FirstOrDefault()?.Id ?? 0;

            return new GetItOptions
            {
                ApplicationId = applicationId,
                RootFolderPath = root,
                QualityProfileId = profile,
                MetadataProfileId = received.MediaKind == MediaKind.Music ? choices.MetadataProfiles.FirstOrDefault()?.Id : null,
                SeriesType = received.MediaKind == MediaKind.Tv ? received.SeriesType ?? "standard" : null,
                SeasonFolder = true
            };
        }

        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            {
                return false;
            }

            var p = path.Replace('\\', '/').TrimEnd('/') + "/";
            var r = root.Replace('\\', '/').TrimEnd('/') + "/";

            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        private ILibraryAdder Adder(ApplicationDefinition definition)
        {
            return _applicationFactory.GetInstance(definition) as ILibraryAdder
                   ?? throw new GetItException($"Nodearr cannot add to {definition.Name}");
        }

        private void CheckOptionsForApp(GetItTitle received, ApplicationDefinition definition, GetItOptions options)
        {
            var request = RequestFor(received) ?? throw new GetItException("Nodearr does not know what the app calls this yet. Wait for your friend's catalog, then try again");

            // Existing titles keep their own settings; the card does not ask for add choices.
            if (ExistingArrId(received, request, definition.Id).HasValue)
            {
                return;
            }

            LibraryAddChoices choices;

            try
            {
                choices = Adder(definition).GetAddChoices();
            }
            catch (Exception ex) when (ex is HttpException or System.Net.Http.HttpRequestException or Newtonsoft.Json.JsonException)
            {
                _logger.Warn(ex, "Unable to check how to add {0} to {1}", received.Title, definition.Name);
                throw new GetItException($"{definition.Name} did not answer. Check its connection, then try again");
            }

            if (string.IsNullOrWhiteSpace(options.RootFolderPath) || !choices.RootFolders.Any(f => string.Equals(f.Path, options.RootFolderPath, StringComparison.Ordinal)))
            {
                throw new GetItException($"That root folder is not available in {definition.Name}. Pick a root folder again");
            }

            if (!choices.QualityProfiles.Any(p => p.Id == options.QualityProfileId))
            {
                throw new GetItException($"That quality profile is not available in {definition.Name}. Pick a quality profile again");
            }

            if (received.MediaKind == MediaKind.Music && !choices.MetadataProfiles.Any(p => p.Id == options.MetadataProfileId))
            {
                throw new GetItException($"That metadata profile is not available in {definition.Name}. Pick a metadata profile again");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;

namespace NzbDrone.Core.Sharing
{
    public class GetItException : NzbDroneException
    {
        public GetItException(string message)
            : base(message)
        {
        }
    }

    // How to add what was sent, where the Arr app does not have it yet.
    public class GetItOptions
    {
        public int ApplicationId { get; set; }
        public string RootFolderPath { get; set; }
        public int QualityProfileId { get; set; }
        public int? MetadataProfileId { get; set; }
        public string SeriesType { get; set; }
        public bool SeasonFolder { get; set; } = true;
    }

    // One local Arr app what was sent could go to.
    public class GetItTarget
    {
        public int ApplicationId { get; set; }
        public string ApplicationName { get; set; }
        public string Implementation { get; set; }
        public bool InLibrary { get; set; }
        public LibraryAddChoices Choices { get; set; }
        public GetItOptions Defaults { get; set; }

        // Why it cannot be used now, such as the app not answering.
        public string Problem { get; set; }
    }

    public class GetItPlan
    {
        public List<GetItTarget> Targets { get; set; } = new();

        // Why nothing can be added, when that is so.
        public string Problem { get; set; }
    }

    public class GetItCommand : Command
    {
        public int ReceivedTitleId { get; set; }
        public int ApplicationId { get; set; }
        public string RootFolderPath { get; set; }
        public int QualityProfileId { get; set; }
        public int? MetadataProfileId { get; set; }
        public string SeriesType { get; set; }
        public bool SeasonFolder { get; set; } = true;

        public override bool SendUpdatesToClient => true;
        public override bool UpdateScheduledTask => false;
    }

    public interface IGetItService
    {
        GetItPlan Plan(int receivedTitleId);
        void GetIt(int receivedTitleId, GetItOptions options);
    }

    // Get it: what a friend sent is added to the right local Arr app if it is missing, what was
    // sent of it is monitored, and the app searches for it. The search finds it in the friend's
    // catalog through Nodearr, as any search does. Nothing is added until the person presses it.
    //
    // Adding takes the choices the person would make in the Arr app, filled in the way most of
    // their library already is: the root folder most of it is in, the quality profile most of it
    // uses, the series type the friend's Sonarr gives the series.
    public class GetItService : IGetItService, IExecute<GetItCommand>, IHandle<ApplicationStartedEvent>
    {
        private readonly IReceivedTitleRepository _received;
        private readonly IPeerRepository _peers;
        private readonly IPeerCatalogRepository _peerItems;
        private readonly IApplicationFactory _applicationFactory;
        private readonly ICatalogSeriesRepository _series;
        private readonly ICatalogMovieRepository _movies;
        private readonly ICatalogArtistRepository _artists;
        private readonly IManageCommandQueue _commandQueue;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public GetItService(IReceivedTitleRepository received,
                            IPeerRepository peers,
                            IPeerCatalogRepository peerItems,
                            IApplicationFactory applicationFactory,
                            ICatalogSeriesRepository series,
                            ICatalogMovieRepository movies,
                            ICatalogArtistRepository artists,
                            IManageCommandQueue commandQueue,
                            IEventAggregator eventAggregator,
                            Logger logger)
        {
            _received = received;
            _peers = peers;
            _peerItems = peerItems;
            _applicationFactory = applicationFactory;
            _series = series;
            _movies = movies;
            _artists = artists;
            _commandQueue = commandQueue;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public GetItPlan Plan(int receivedTitleId)
        {
            var received = _received.Get(receivedTitleId);
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

                if (target.Problem == null && !target.InLibrary && (target.Choices.RootFolders.Count == 0 || target.Choices.QualityProfiles.Count == 0))
                {
                    target.Problem = $"{definition.Name} has no root folder yet. Add one in {definition.Name}, then try again";
                }

                plan.Targets.Add(target);
            }

            return plan;
        }

        public void GetIt(int receivedTitleId, GetItOptions options)
        {
            var received = _received.Get(receivedTitleId);

            if (received.GetItState == GetItState.Working)
            {
                throw new GetItException("This is being added already");
            }

            var peer = _peers.Find(received.PeerId);

            if (peer is not { State: PeerState.Trusted } || received.Handled.HasValue)
            {
                throw new GetItException("This is no longer waiting");
            }

            var definition = _applicationFactory.Find(options.ApplicationId);

            if (definition == null || definition.Implementation != AppFor(received))
            {
                throw new GetItException("Choose an app this can be added to");
            }

            SetState(received, GetItState.Working, $"Asking {definition.Name} to add it");

            _commandQueue.Push(new GetItCommand
            {
                ReceivedTitleId = received.Id,
                ApplicationId = definition.Id,
                RootFolderPath = options.RootFolderPath,
                QualityProfileId = options.QualityProfileId,
                MetadataProfileId = options.MetadataProfileId,
                SeriesType = options.SeriesType,
                SeasonFolder = options.SeasonFolder
            });
        }

        public void Execute(GetItCommand message)
        {
            var received = _received.Find(message.ReceivedTitleId);

            if (received == null)
            {
                return;
            }

            try
            {
                var definition = _applicationFactory.Get(message.ApplicationId);
                var request = RequestFor(received) ?? throw new GetItException("Nodearr does not know what the app calls this yet");

                request.ExistingArrId = ExistingArrId(received, request, definition.Id);
                request.RootFolderPath = message.RootFolderPath;
                request.QualityProfileId = message.QualityProfileId;
                request.MetadataProfileId = message.MetadataProfileId;
                request.SeriesType = message.SeriesType ?? received.SeriesType;
                request.SeasonFolder = message.SeasonFolder;

                var done = Adder(definition).AddAndSearch(request);

                _logger.Info("{0}: {1}", received.Title, done);
                SetState(received, GetItState.Done, done);
            }
            catch (Exception ex) when (ex is LibraryAddException or GetItException)
            {
                SetState(received, GetItState.Failed, ex.Message);
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex, "Unable to add {0}", received.Title);
                SetState(received, GetItState.Failed, $"The app answered {(int)ex.Response.StatusCode}: {Summary(ex.Response.Content)}");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to add {0}", received.Title);
                SetState(received, GetItState.Failed, ex.Message);
            }
        }

        // One that was being added when Nodearr stopped will not finish now.
        public void Handle(ApplicationStartedEvent message)
        {
            foreach (var received in _received.All().Where(r => r.GetItState == GetItState.Working))
            {
                SetState(received, GetItState.Failed, "Nodearr stopped before it finished. Try again");
            }
        }

        // The app a title of this kind goes to. Adult content goes to Whisparr v3 only.
        private static string AppFor(ReceivedTitle received)
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
        private LibraryAddRequest RequestFor(ReceivedTitle received)
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
            var items = new Lazy<List<PeerCatalogItem>>(() => _peerItems.ForPeer(received.PeerId).Where(i => offered.Contains(i.RemoteId)).ToList());
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
                    request.OfferedEpisodes = _peerItems.ForPeer(received.PeerId)
                        .Where(i => i.MediaKind == MediaKind.Tv && i.TvdbId > 0 && i.TvdbId == request.TvdbId)
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

                    request.OfferedAlbums = _peerItems.ForPeer(received.PeerId)
                        .Where(i => i.ForeignArtistId != null && string.Equals(i.ForeignArtistId, request.ForeignArtistId, StringComparison.OrdinalIgnoreCase) && i.ForeignAlbumId != null)
                        .Select(i => i.ForeignAlbumId)
                        .Distinct()
                        .ToList();

                    return request.ForeignArtistId != null ? request : null;

                default:
                    return null;
            }
        }

        private int? ExistingArrId(ReceivedTitle received, LibraryAddRequest request, int applicationId)
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
        private static bool IsSameMovie(CatalogMovie movie, ReceivedTitle received, LibraryAddRequest request)
        {
            return (request.TmdbId > 0 && movie.TmdbId == request.TmdbId) ||
                   (request.ImdbId != null && movie.ImdbId == request.ImdbId) ||
                   (received.IsAdult && request.StashId != null && string.Equals(movie.StashId, request.StashId, StringComparison.OrdinalIgnoreCase)) ||
                   (received.IsAdult && request.TpdbId != null && string.Equals(movie.TpdbId, request.TpdbId, StringComparison.OrdinalIgnoreCase)) ||
                   (received.IsAdult && received.ForeignId != null && movie.ForeignId == received.ForeignId);
        }

        // A scene a friend's Whisparr v2 holds, known only by v2's own number for it.
        private bool IsWhisparrV2Scene(ReceivedTitle received)
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

            return _peerItems.ForPeer(received.PeerId).Any(i => offered.Contains(i.RemoteId) && i.ForeignId?.StartsWith("whisparr2:", StringComparison.Ordinal) == true);
        }

        // The way most of the library already is.
        private GetItOptions Defaults(ReceivedTitle received, int applicationId, LibraryAddChoices choices)
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

        private void SetState(ReceivedTitle received, GetItState state, string message)
        {
            received.GetItState = state;
            received.GetItMessage = message;
            _received.SetFields(received, r => r.GetItState, r => r.GetItMessage);
            _eventAggregator.PublishEvent(new ReceivedTitlesChangedEvent());
        }

        // An Arr app's error answer, short enough to show.
        private static string Summary(string content)
        {
            var text = (content ?? string.Empty).Trim();

            return text.Length > 300 ? text[..300] : text;
        }
    }
}

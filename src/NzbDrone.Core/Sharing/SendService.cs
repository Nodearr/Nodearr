using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NLog;
using NzbDrone.Common.Exceptions;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Sharing
{
    public class SendException : NzbDroneException
    {
        public SendException(string message)
            : base(message)
        {
        }
    }

    public class SendWithInviteResult
    {
        public SentTitle Sent { get; set; }
        public CreatedInvite Invite { get; set; }
    }

    public interface ISendService
    {
        List<SentTitle> All();

        // Shares one title, or parts of it, with a friend, and tells their node.
        SentTitle Send(int peerId, IEnumerable<ShareKey> items);

        // The same for someone who is not a friend yet: an invite of its own, and the send waits
        // for it to be used.
        SendWithInviteResult SendWithInvite(IEnumerable<ShareKey> items, TimeSpan validFor);

        // Forgets a send. What it shared stays shared: that is the friend's list to change.
        void Delete(int id);
    }

    // Sending something is sharing it, on a list of the friend's own ("Sent to <name>"), and then
    // telling their node, so it can offer to fetch it. The share is what lets them fetch it; the
    // note only says it is there. A node that cannot be told yet is tried again each time the
    // connection to it is checked.
    public class SendService : ISendService,
                               IHandle<InviteRedeemedEvent>,
                               IHandleAsync<SendQueuedEvent>,
                               IHandleAsync<PeerConnectionCheckedEvent>,
                               IHandle<PeerDeletedEvent>
    {
        // A node that has not taken the note in this long is not going to: an older Nodearr
        // refuses it. What was sent is shared with it either way.
        public static readonly TimeSpan GiveUpAfter = TimeSpan.FromDays(30);

        // A node still finishing a pairing refuses the note, and so does an older Nodearr. The
        // first will take it at the next connection check.
        private const string NotTaken = "Their Nodearr did not take the note yet. It is shared with them either way, and shows in their catalog";

        private readonly ISentTitleRepository _sent;
        private readonly ISharingService _sharing;
        private readonly IPeerService _peerService;
        private readonly IPeerRepository _peers;
        private readonly ICatalogSeriesRepository _series;
        private readonly ICatalogEpisodeRepository _episodes;
        private readonly ICatalogMovieRepository _movies;
        private readonly ICatalogArtistRepository _artists;
        private readonly ICatalogAlbumRepository _albums;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly HashSet<int> _delivering = new();

        public SendService(ISentTitleRepository sent,
                           ISharingService sharing,
                           IPeerService peerService,
                           IPeerRepository peers,
                           ICatalogSeriesRepository series,
                           ICatalogEpisodeRepository episodes,
                           ICatalogMovieRepository movies,
                           ICatalogArtistRepository artists,
                           ICatalogAlbumRepository albums,
                           IEventAggregator eventAggregator,
                           Logger logger)
        {
            _sent = sent;
            _sharing = sharing;
            _peerService = peerService;
            _peers = peers;
            _series = series;
            _episodes = episodes;
            _movies = movies;
            _artists = artists;
            _albums = albums;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        // The other node finishes pairing a moment after this one does (PeerConnectionUpkeep).
        public TimeSpan PairingSettle { get; set; } = TimeSpan.FromSeconds(3);

        public List<SentTitle> All()
        {
            return _sent.All().OrderByDescending(s => s.Created).ToList();
        }

        public SentTitle Send(int peerId, IEnumerable<ShareKey> items)
        {
            var peer = _peers.Find(peerId);

            if (peer is not { State: PeerState.Trusted })
            {
                throw new SendException("Only a friend can be sent something. Pair with them first, or send it with an invite");
            }

            var sent = Record(Check(items), s => s.PeerId = peer.Id);

            Share(sent, peer);
            _eventAggregator.PublishEvent(new SendQueuedEvent(sent.Id));

            return sent;
        }

        public SendWithInviteResult SendWithInvite(IEnumerable<ShareKey> items, TimeSpan validFor)
        {
            var checkedItems = Check(items);

            // Nothing else is shared with whoever uses this invite: only what was sent.
            var invite = _peerService.CreateInvite(validFor, $"For {checkedItems.Title}", false);
            var sent = Record(checkedItems, s => s.InviteId = invite.Invite.Id);

            return new SendWithInviteResult { Sent = sent, Invite = invite };
        }

        public void Delete(int id)
        {
            var sent = _sent.Get(id);

            // An invite made for this send has no other use.
            if (sent.PeerId == null && sent.InviteId.HasValue)
            {
                _peerService.DeleteInvite(sent.InviteId.Value);
            }

            _sent.Delete(id);
            _eventAggregator.PublishEvent(new SentTitlesChangedEvent());
        }

        // Handled as the invite is used, before the other node is answered, so the first catalog
        // it asks for already holds what was sent.
        public void Handle(InviteRedeemedEvent message)
        {
            foreach (var sent in _sent.WaitingForInvite(message.Invite.Id))
            {
                sent.PeerId = message.Peer.Id;
                _sent.Update(sent);
                Share(sent, message.Peer);
                _eventAggregator.PublishEvent(new SendQueuedEvent(sent.Id, true));
            }
        }

        public void HandleAsync(SendQueuedEvent message)
        {
            if (message.AfterPairing)
            {
                Thread.Sleep(PairingSettle);
            }

            Deliver(message.SentTitleId);
        }

        // Each check that reaches the node is a chance to tell it what it has not been told.
        public void HandleAsync(PeerConnectionCheckedEvent message)
        {
            if (message.Peer.State != PeerState.Trusted || message.Peer.LastError != null)
            {
                return;
            }

            var since = DateTime.UtcNow - GiveUpAfter;

            foreach (var sent in _sent.Undelivered(message.Peer.Id).Where(s => s.Created > since))
            {
                Deliver(sent.Id);
            }
        }

        public void Handle(PeerDeletedEvent message)
        {
            foreach (var list in _sharing.All().Where(l => l.List.SentToPeerId == message.PeerId))
            {
                _sharing.Delete(list.List.Id);
            }

            _sent.DeleteForPeer(message.PeerId);
            _eventAggregator.PublishEvent(new SentTitlesChangedEvent());
        }

        // One title, or parts of it, with at least one file that can be sent.
        private (List<ShareKey> Keys, string Title, string Summary) Check(IEnumerable<ShareKey> items)
        {
            var keys = items?.Distinct().ToList() ?? new List<ShareKey>();
            var (title, summary) = Describe(keys);

            if (_sharing.OfferIdsFor(keys).Count == 0)
            {
                throw new SendException(IsHidden(keys)
                    ? "That is on your Hidden list, which keeps it from everyone"
                    : "There is no file of that in your library to send yet");
            }

            return (keys, title, summary);
        }

        private SentTitle Record((List<ShareKey> Keys, string Title, string Summary) items, Action<SentTitle> to)
        {
            var sent = new SentTitle
            {
                SendId = Guid.NewGuid().ToString("N"),
                Title = items.Title,
                Summary = items.Summary,
                MediaKind = items.Keys[0].Kind,
                Items = JsonSerializer.Serialize(items.Keys),
                Created = DateTime.UtcNow
            };

            to(sent);
            _sent.Insert(sent);
            _eventAggregator.PublishEvent(new SentTitlesChangedEvent());

            return sent;
        }

        // On the friend's own list, which is made the first time they are sent something.
        private void Share(SentTitle sent, Peer peer)
        {
            var list = _sharing.All().FirstOrDefault(l => l.List.SentToPeerId == peer.Id)
                       ?? _sharing.Create($"Sent to {peer.Name}", peer.Id);

            _sharing.Update(list.List.Id, null, list.Items.Concat(sent.GetItems()), list.PeerIds.Append(peer.Id));
        }

        private void Deliver(int sentTitleId)
        {
            lock (_delivering)
            {
                if (!_delivering.Add(sentTitleId))
                {
                    return;
                }
            }

            try
            {
                var sent = _sent.Find(sentTitleId);

                if (sent is not { DeliveredAt: null, PeerId: not null })
                {
                    return;
                }

                var peer = _peers.Find(sent.PeerId.Value);

                if (peer is not { State: PeerState.Trusted })
                {
                    return;
                }

                var keys = sent.GetItems();
                var message = new PeerMessage
                {
                    Type = PeerMessageTypes.Sent,
                    Sent = new SentMessage
                    {
                        SendId = sent.SendId,
                        Title = sent.Title,
                        Summary = sent.Summary,
                        MediaKind = sent.MediaKind,
                        IsWhole = keys.Any(IsWholeTitle),
                        OfferIds = _sharing.OfferIdsFor(keys)
                    }
                };

                Identify(message.Sent, keys);

                try
                {
                    var reply = _peerService.Ask(peer, message);

                    if (reply is { Ok: true })
                    {
                        sent.DeliveredAt = DateTime.UtcNow;
                        sent.LastError = null;
                        _logger.Info("Told {0} about {1}", peer.Name, sent.Title);
                    }
                    else
                    {
                        sent.LastError = NotTaken;
                    }
                }
                catch (TransportException ex)
                {
                    sent.LastError = ex.Failure == TransportFailure.NotAuthorized ? NotTaken : ex.Message;
                    _logger.Debug(ex, "Unable to tell {0} about {1} yet", peer.Name, sent.Title);
                }
                catch (PeerException ex)
                {
                    sent.LastError = ex.Message;
                }

                _sent.Update(sent);
                _eventAggregator.PublishEvent(new SentTitlesChangedEvent());
            }
            finally
            {
                lock (_delivering)
                {
                    _delivering.Remove(sentTitleId);
                }
            }
        }

        // What the friend's Arr app needs to add it: the title's IDs, and the parts sent.
        private void Identify(SentMessage message, List<ShareKey> keys)
        {
            var first = keys[0];
            var ids = new[] { first.ArrId };
            var parts = new SentParts();

            switch (first.Kind)
            {
                case MediaKind.Tv:
                    var series = _series.ForArrIds(first.ApplicationId, ids).FirstOrDefault();

                    if (series != null)
                    {
                        (message.TvdbId, message.TmdbId, message.ImdbId, message.Year, message.SeriesType) = (series.TvdbId, series.TmdbId, series.ImdbId, series.Year, series.SeriesType);
                    }

                    parts.Seasons = keys.Where(k => k.SeasonNumber.HasValue).Select(k => k.SeasonNumber.Value).Distinct().OrderBy(n => n).ToList();

                    var episodeIds = keys.Where(k => k.EpisodeArrId.HasValue).Select(k => k.EpisodeArrId.Value).ToHashSet();

                    if (episodeIds.Count > 0)
                    {
                        parts.Episodes = _episodes.ForSeries(first.ApplicationId, first.ArrId)
                            .Where(e => episodeIds.Contains(e.ArrId))
                            .OrderBy(e => e.SeasonNumber)
                            .ThenBy(e => e.EpisodeNumber)
                            .Select(e => new SeasonEpisode { SeasonNumber = e.SeasonNumber, EpisodeNumber = e.EpisodeNumber })
                            .ToList();
                    }

                    break;

                case MediaKind.Movie:
                    var movie = _movies.ForArrIds(first.ApplicationId, ids).FirstOrDefault();

                    if (movie != null)
                    {
                        (message.TmdbId, message.ImdbId, message.Year, message.IsAdult) = (movie.TmdbId, movie.ImdbId, movie.Year, movie.IsAdult);
                    }

                    break;

                case MediaKind.Music:
                    message.ForeignArtistId = _artists.ForArrIds(first.ApplicationId, ids).FirstOrDefault()?.ForeignArtistId;

                    var albumIds = keys.Where(k => k.EpisodeArrId.HasValue).Select(k => k.EpisodeArrId.Value).ToList();

                    parts.Albums = _albums.ForArrIds(first.ApplicationId, albumIds).Select(a => a.ForeignAlbumId).Where(a => a != null).ToList();

                    break;
            }

            message.Parts = parts;
        }

        private static bool IsWholeTitle(ShareKey key)
        {
            return key.SeasonNumber == null && key.EpisodeArrId == null;
        }

        private bool IsHidden(List<ShareKey> keys)
        {
            var hidden = _sharing.All().Where(l => l.List.IsHidden).SelectMany(l => l.Items).ToHashSet();
            var whole = new ShareKey(keys[0].Kind, keys[0].ApplicationId, keys[0].ArrId);

            return hidden.Contains(whole) || keys.All(hidden.Contains);
        }

        // One title, as the library names it, and which part of it was chosen.
        private (string Title, string Summary) Describe(List<ShareKey> keys)
        {
            if (keys.Count == 0)
            {
                throw new SendException("Choose something to send");
            }

            var first = keys[0];

            if (keys.Any(k => k.Kind != first.Kind || k.ApplicationId != first.ApplicationId || k.ArrId != first.ArrId))
            {
                throw new SendException("Send one title at a time");
            }

            switch (first.Kind)
            {
                case MediaKind.Tv:
                    var series = _series.ForArrIds(first.ApplicationId, new[] { first.ArrId }).FirstOrDefault(s => s.MissingSince == null)
                                 ?? throw new SendException("That series is no longer in your library");

                    return (series.Title, keys.Any(IsWholeTitle) ? null : SeriesSummary(first, keys));

                case MediaKind.Movie:
                    var movie = _movies.ForArrIds(first.ApplicationId, new[] { first.ArrId }).FirstOrDefault(m => m.MissingSince == null)
                                ?? throw new SendException("That movie is no longer in your library");

                    return (movie.Title, null);

                case MediaKind.Music:
                    var artist = _artists.ForArrIds(first.ApplicationId, new[] { first.ArrId }).FirstOrDefault(a => a.MissingSince == null)
                                 ?? throw new SendException("That artist is no longer in your library");

                    return (artist.Name, keys.Any(IsWholeTitle) ? null : AlbumSummary(first, keys));

                default:
                    throw new SendException("That cannot be sent");
            }
        }

        // "Seasons 1 and 2", "S01E03", "Season 1, plus 3 episodes".
        private string SeriesSummary(ShareKey series, List<ShareKey> keys)
        {
            var seasons = keys.Where(k => k.SeasonNumber.HasValue).Select(k => k.SeasonNumber.Value).Distinct().OrderBy(n => n).ToList();
            var episodeIds = keys.Where(k => k.EpisodeArrId.HasValue).Select(k => k.EpisodeArrId.Value).ToHashSet();
            var parts = new List<string>();

            if (seasons.Count > 0)
            {
                parts.Add((seasons.Count == 1 ? "Season " : "Seasons ") + Join(seasons.Select(n => n.ToString()).ToList()));
            }

            if (episodeIds.Count == 1)
            {
                var episode = _episodes.ForSeries(series.ApplicationId, series.ArrId).FirstOrDefault(e => episodeIds.Contains(e.ArrId));

                parts.Add(episode == null ? "1 episode" : $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}");
            }
            else if (episodeIds.Count > 1)
            {
                parts.Add($"{episodeIds.Count} episodes");
            }

            return string.Join(", plus ", parts);
        }

        private string AlbumSummary(ShareKey artist, List<ShareKey> keys)
        {
            var albumIds = keys.Where(k => k.EpisodeArrId.HasValue).Select(k => k.EpisodeArrId.Value).ToList();

            if (albumIds.Count != 1)
            {
                return $"{albumIds.Count} albums";
            }

            return _albums.ForArrIds(artist.ApplicationId, albumIds).FirstOrDefault()?.Title ?? "1 album";
        }

        private static string Join(List<string> items)
        {
            return items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
        }
    }
}

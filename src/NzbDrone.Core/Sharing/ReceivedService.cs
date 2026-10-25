using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;

namespace NzbDrone.Core.Sharing
{
    public interface IReceivedService
    {
        // What friends sent that has not been acted on or put away, newest first.
        List<ReceivedTitle> Waiting();
        void Dismiss(int id);
    }

    // Keeps what friends send this node, for the person to act on. The note says only what was
    // sent. The files come in the friend's catalog, which is fetched again as the note arrives.
    public class ReceivedService : IReceivedService, IHandle<TitlesSentEvent>, IHandle<PeerDeletedEvent>
    {
        // More waiting from one friend than anyone would read is not kept.
        public const int MaxWaitingPerPeer = 200;

        private const int MaxOfferIds = 20000;
        private const int MaxText = 300;
        private const int MaxParts = 10000;

        private static readonly Regex SendIdPattern = new(@"^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);
        private static readonly Regex ForeignIdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9:._-]{0,99}$", RegexOptions.Compiled);
        private static readonly Regex ImdbPattern = new(@"^tt\d{1,10}$", RegexOptions.Compiled);
        private static readonly Regex OfferIdPattern = new(@"^[ema]-\d{1,9}-\d{1,9}$", RegexOptions.Compiled);

        private readonly IReceivedTitleRepository _received;
        private readonly IPeerRepository _peers;
        private readonly IPeerCatalogRepository _peerItems;
        private readonly ILocalHoldingsFactory _holdings;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ReceivedService(IReceivedTitleRepository received, IPeerRepository peers, IPeerCatalogRepository peerItems, ILocalHoldingsFactory holdings, IEventAggregator eventAggregator, Logger logger)
        {
            _received = received;
            _peers = peers;
            _peerItems = peerItems;
            _holdings = holdings;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public List<ReceivedTitle> Waiting()
        {
            var trusted = _peers.All().Where(p => p.State == PeerState.Trusted).Select(p => p.Id).ToHashSet();

            var waiting = _received.All()
                .Where(r => r.Handled == null && trusted.Contains(r.PeerId))
                .OrderByDescending(r => r.Received)
                .ToList();

            var now = DateTime.UtcNow;
            var holdings = new Lazy<LocalHoldings>(() => _holdings.Load());

            foreach (var received in waiting.Where(r => r.GetItState == GetItState.Done))
            {
                if (received.GetItCompletedAt <= now.AddHours(-24) || HasSentFiles(received, holdings))
                {
                    received.Handled = now;
                    _received.SetFields(received, r => r.Handled);
                }
            }

            return waiting.Where(r => r.Handled == null).ToList();
        }

        public void Dismiss(int id)
        {
            var received = _received.Get(id);

            received.Handled = DateTime.UtcNow;
            _received.SetFields(received, r => r.Handled);
            _eventAggregator.PublishEvent(new ReceivedTitlesChangedEvent());
        }

        private bool HasSentFiles(ReceivedTitle received, Lazy<LocalHoldings> holdings)
        {
            var offerIds = (received.OfferIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            var items = _peerItems.ForRemoteIds(received.PeerId, offerIds);

            // A partial or withdrawn offer cannot prove that everything sent has arrived.
            return offerIds.Count > 0 && items.Count == offerIds.Count && items.All(i => holdings.Value.Classify(i) == PeerItemStatus.Have);
        }

        // Handled before the friend is answered, so an answer means it was kept. The same send
        // told twice is kept once.
        public void Handle(TitlesSentEvent message)
        {
            var sent = message.Sent;

            if (!SendIdPattern.IsMatch(sent.SendId) || !Enum.IsDefined(sent.MediaKind))
            {
                _logger.Debug("Ignored a send from {0} that would not do", message.Peer.Name);
                return;
            }

            var existing = _received.Find(message.Peer.Id, sent.SendId);

            if (existing == null && _received.CountWaiting(message.Peer.Id) >= MaxWaitingPerPeer)
            {
                _logger.Warn("{0} has sent more than {1} things that are waiting here. Later ones are not kept", message.Peer.Name, MaxWaitingPerPeer);
                return;
            }

            var received = existing ?? new ReceivedTitle { PeerId = message.Peer.Id, SendId = sent.SendId, Received = DateTime.UtcNow };

            received.Title = Text(sent.Title) ?? "Something";
            received.Summary = Text(sent.Summary);
            received.MediaKind = sent.MediaKind;
            received.IsWhole = sent.IsWhole;
            received.OfferIds = string.Join(",", (sent.OfferIds ?? new List<string>()).Where(i => i != null && OfferIdPattern.IsMatch(i)).Distinct().Take(MaxOfferIds));
            Identify(received, sent);

            if (existing == null)
            {
                _received.Insert(received);
                _logger.Info("{0} sent {1}", message.Peer.Name, received.Title);
            }
            else
            {
                _received.Update(received);
            }

            _eventAggregator.PublishEvent(new ReceivedTitlesChangedEvent());

            // What was sent is in the friend's catalog now, not at the next Catalog Sync.
            _eventAggregator.PublishEvent(new PeerCatalogChangedEvent(message.Peer.Id));
        }

        public void Handle(PeerDeletedEvent message)
        {
            _received.DeleteForPeer(message.PeerId);
            _eventAggregator.PublishEvent(new ReceivedTitlesChangedEvent());
        }

        // The IDs and parts Get it adds the title by, each checked, and dropped if it would not do.
        private static void Identify(ReceivedTitle received, SentMessage sent)
        {
            received.Year = sent.Year is > 1800 and < 3000 ? sent.Year : 0;
            received.TvdbId = sent.TvdbId > 0 ? sent.TvdbId : 0;
            received.TmdbId = sent.TmdbId > 0 ? sent.TmdbId : 0;
            received.ImdbId = sent.ImdbId != null && ImdbPattern.IsMatch(sent.ImdbId) ? sent.ImdbId : null;
            received.ForeignArtistId = Guid.TryParse(sent.ForeignArtistId, out var artist) ? artist.ToString() : null;
            received.IsAdult = sent.IsAdult && sent.MediaKind == MediaKind.Movie;
            received.SeriesType = sent.SeriesType is "standard" or "daily" or "anime" ? sent.SeriesType : null;
            received.ForeignId = received.IsAdult ? Id(sent.ForeignId) : null;
            received.StashId = received.IsAdult ? Id(sent.StashId) : null;
            received.TpdbId = received.IsAdult ? Id(sent.TpdbId) : null;

            var parts = sent.Parts ?? new SentParts();
            var kept = new SentParts
            {
                Seasons = (parts.Seasons ?? new List<int>()).Where(n => n is >= 0 and < 10000).Distinct().Take(MaxParts).ToList(),
                Episodes = (parts.Episodes ?? new List<SeasonEpisode>())
                    .Where(e => e != null && e.SeasonNumber is >= 0 and < 10000 && e.EpisodeNumber is >= 0 and < 100000)
                    .Take(MaxParts)
                    .ToList(),
                Albums = (parts.Albums ?? new List<string>()).Select(a => Guid.TryParse(a, out var g) ? g.ToString() : null).Where(a => a != null).Distinct().Take(MaxParts).ToList()
            };

            received.Parts = kept.Seasons.Count + kept.Episodes.Count + kept.Albums.Count == 0 ? null : JsonSerializer.Serialize(kept);
        }

        // An ID as Whisparr's metadata gives one: a UUID, a number, or such with a prefix.
        private static string Id(string value)
        {
            return value != null && ForeignIdPattern.IsMatch(value) ? value : null;
        }

        private static string Text(string value)
        {
            var trimmed = value?.Trim();

            return trimmed is { Length: > 0 and <= MaxText } && !trimmed.Any(char.IsControl) ? trimmed : null;
        }
    }
}

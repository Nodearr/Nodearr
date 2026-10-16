using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
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

        private static readonly Regex SendIdPattern = new(@"^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);
        private static readonly Regex OfferIdPattern = new(@"^[ema]-\d{1,9}-\d{1,9}$", RegexOptions.Compiled);

        private readonly IReceivedTitleRepository _received;
        private readonly IPeerRepository _peers;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ReceivedService(IReceivedTitleRepository received, IPeerRepository peers, IEventAggregator eventAggregator, Logger logger)
        {
            _received = received;
            _peers = peers;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public List<ReceivedTitle> Waiting()
        {
            var trusted = _peers.All().Where(p => p.State == PeerState.Trusted).Select(p => p.Id).ToHashSet();

            return _received.All()
                .Where(r => r.Handled == null && trusted.Contains(r.PeerId))
                .OrderByDescending(r => r.Received)
                .ToList();
        }

        public void Dismiss(int id)
        {
            var received = _received.Get(id);

            received.Handled = DateTime.UtcNow;
            _received.Update(received);
            _eventAggregator.PublishEvent(new ReceivedTitlesChangedEvent());
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

        private static string Text(string value)
        {
            var trimmed = value?.Trim();

            return trimmed is { Length: > 0 and <= MaxText } && !trimmed.Any(char.IsControl) ? trimmed : null;
        }
    }
}

using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Sharing
{
    // Raised when what this node shares has changed.
    public class SharingChangedEvent : IEvent
    {
    }

    // Tells a trusted peer to fetch this node's catalog again when something it holds is no longer
    // offered to it: unshared, hidden, or gone from the Arr app. That way an unshared file leaves
    // the peer's searches within seconds. What is newly offered waits for the peer's next Catalog
    // Sync, as a new release waits for Sonarr's next RSS Sync.
    //
    // The hint carries no content. Each peer asks, and is answered for itself alone.
    public class SharingNotifier : IHandleAsync<SharingChangedEvent>, IHandleAsync<CatalogRefreshedEvent>
    {
        private readonly IPeerRepository _peers;
        private readonly IPeerService _peerService;
        private readonly ISharingService _sharing;
        private readonly IOfferRecordRepository _records;
        private readonly Logger _logger;

        public SharingNotifier(IPeerRepository peers, IPeerService peerService, ISharingService sharing, IOfferRecordRepository records, Logger logger)
        {
            _peers = peers;
            _peerService = peerService;
            _sharing = sharing;
            _records = records;
            _logger = logger;
        }

        public void HandleAsync(SharingChangedEvent message)
        {
            TellAboutRemovals();
        }

        public void HandleAsync(CatalogRefreshedEvent message)
        {
            TellAboutRemovals();
        }

        private void TellAboutRemovals()
        {
            foreach (var peer in _peers.All().Where(p => p.State == PeerState.Trusted))
            {
                // What this node last sent the peer. Nothing sent, nothing to take back.
                var sent = _records.FindForPeer(peer.Id)?.GetFingerprints();

                if (sent == null || sent.Count == 0)
                {
                    continue;
                }

                var offered = _sharing.BuildOffer(peer, true).Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

                if (sent.Keys.All(offered.Contains))
                {
                    continue;
                }

                try
                {
                    _peerService.Ask(peer, new PeerMessage { Type = PeerMessageTypes.CatalogChanged });
                }
                catch (Exception ex) when (ex is TransportException or PeerException)
                {
                    // It will find out at its next Catalog Sync.
                    _logger.Debug(ex, "Unable to tell {0} that files were taken back", peer.Name);
                }
            }
        }
    }
}

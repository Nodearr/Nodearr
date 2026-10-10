using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Sharing
{
    // Raised when what this node shares has changed.
    public class SharingChangedEvent : IEvent
    {
    }

    // Tells trusted peers to fetch this node's catalog again when sharing changes, so something
    // that was just unshared stops showing on their side promptly instead of at their next sync.
    // The hint carries no content. Each peer asks, and is answered for itself alone.
    public class SharingNotifier : IHandleAsync<SharingChangedEvent>
    {
        private readonly IPeerRepository _peers;
        private readonly IPeerService _peerService;
        private readonly Logger _logger;

        public SharingNotifier(IPeerRepository peers, IPeerService peerService, Logger logger)
        {
            _peers = peers;
            _peerService = peerService;
            _logger = logger;
        }

        public void HandleAsync(SharingChangedEvent message)
        {
            foreach (var peer in _peers.All().Where(p => p.State == PeerState.Trusted))
            {
                try
                {
                    _peerService.Ask(peer, new PeerMessage { Type = PeerMessageTypes.CatalogChanged });
                }
                catch (Exception ex) when (ex is TransportException or PeerException)
                {
                    // It will find out at its next scheduled sync.
                    _logger.Debug(ex, "Unable to tell {0} that sharing changed", peer.Name);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Nodes
{
    // Checks the connection to every trusted node. Scheduled.
    public class CheckPeerConnectionsCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }

    // Keeps what is known about the route to each trusted node current without anyone pressing
    // "Check connection": on a schedule, when two nodes have just paired, and when a transfer has
    // just shown that the route is not what the last check said.
    public class PeerConnectionUpkeep : IExecute<CheckPeerConnectionsCommand>,
                                        IHandleAsync<PeerStateChangedEvent>,
                                        IHandleAsync<PeerAddressChangedEvent>,
                                        IHandleAsync<TransferAttemptEndedEvent>
    {
        // A transfer that keeps failing asks for a check each time. One is enough for a while.
        public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(45);

        private readonly IPeerService _peerService;
        private readonly IPeerRepository _peers;
        private readonly IPeerTransport _transport;
        private readonly Logger _logger;
        private readonly HashSet<int> _checking = new();
        private readonly HashSet<int> _addressChangedWhileChecking = new();

        public PeerConnectionUpkeep(IPeerService peerService, IPeerRepository peers, IPeerTransport transport, Logger logger)
        {
            _peerService = peerService;
            _peers = peers;
            _transport = transport;
            _logger = logger;
        }

        // The other node finishes pairing a moment after this one does. Asked sooner than that it
        // would not yet know this node, and would refuse to answer.
        public TimeSpan PairingSettle { get; set; } = TimeSpan.FromSeconds(3);

        public void Execute(CheckPeerConnectionsCommand message)
        {
            if (!_transport.IsRunning)
            {
                return;
            }

            foreach (var peer in _peers.All().Where(p => p.State == PeerState.Trusted).ToList())
            {
                Check(peer.Id);
            }
        }

        public void HandleAsync(PeerStateChangedEvent message)
        {
            if (message.Peer.State != PeerState.Trusted || message.PreviousState == PeerState.Trusted || !_transport.IsRunning)
            {
                return;
            }

            Thread.Sleep(PairingSettle);
            _logger.Info("Checking the connection to {0}, which has just been paired", message.Peer.Name);
            Check(message.Peer.Id);
        }

        public void HandleAsync(TransferAttemptEndedEvent message)
        {
            var peer = _peers.Find(message.PeerId);

            if (peer is not { State: PeerState.Trusted } || !_transport.IsRunning)
            {
                return;
            }

            // A file that arrived came over a direct route, whatever the last check found.
            if (message.Failure == TransferFailure.None)
            {
                if (peer.ConnectionPath != ConnectionPath.Direct)
                {
                    _logger.Info("Checking the connection to {0} again: a file arrived from it, which the last check did not allow for", peer.Name);
                    Check(peer.Id);
                }

                return;
            }

            if (message.Failure is not (TransferFailure.NoDirectRoute or TransferFailure.DirectPathLost or TransferFailure.PeerUnreachable))
            {
                return;
            }

            if (peer.ConnectionChecked.HasValue && DateTime.UtcNow - peer.ConnectionChecked.Value < MinimumGap)
            {
                return;
            }

            _logger.Info("Checking the connection to {0} again, after a transfer from it stopped as {1}", peer.Name, message.Failure);
            Check(peer.Id);
        }

        public void HandleAsync(PeerAddressChangedEvent message)
        {
            var peer = _peers.Find(message.PeerId);

            if (peer is not { State: PeerState.Trusted } || !_transport.IsRunning)
            {
                return;
            }

            _logger.Info("Checking the connection to {0} again, after its address changed", peer.Name);
            Check(peer.Id, addressChanged: true);
        }

        private void Check(int peerId, bool addressChanged = false)
        {
            lock (_checking)
            {
                if (!_checking.Add(peerId))
                {
                    if (addressChanged)
                    {
                        _addressChangedWhileChecking.Add(peerId);
                    }

                    return;
                }
            }

            bool checkAgain;
            do
            {
                try
                {
                    _peerService.Ping(peerId);
                }
                catch (PeerException)
                {
                    // No longer trusted. There is nothing to check.
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to check the connection to node {0}", peerId);
                }
                finally
                {
                    lock (_checking)
                    {
                        checkAgain = _addressChangedWhileChecking.Remove(peerId);
                        if (!checkAgain)
                        {
                            _checking.Remove(peerId);
                        }
                    }
                }
            }
            while (checkAgain);
        }
    }
}

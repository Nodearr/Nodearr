using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Says so when the network this node is on will get in the way of reaching other nodes
    // directly, which is the only way files move.
    [CheckOn(typeof(PeerTransportStateChangedEvent))]
    [CheckOn(typeof(PeerConnectionCheckedEvent))]
    public class PeerNetworkCheck : HealthCheckBase
    {
        private readonly IPeerTransport _transport;
        private readonly IPeerRepository _peers;

        public PeerNetworkCheck(IPeerTransport transport, IPeerRepository peers, ILocalizationService localizationService)
            : base(localizationService)
        {
            _transport = transport;
            _peers = peers;
        }

        public override HealthCheck Check()
        {
            NetworkStatus network;

            try
            {
                network = _transport.GetNetworkStatus();
            }
            catch (TransportException)
            {
                // Not running. PeerTransportCheck says so.
                return new HealthCheck(GetType());
            }

            var trusted = _peers.All().Where(p => p.State == PeerState.Trusted).ToList();

            // The last connection check reached these nodes through a relay and no other way.
            // Transfers with them are certain to fail, whatever else is known about the network.
            // The message names the firewall first: a machine that blocks incoming UDP looks
            // exactly like this, and nothing run on that machine alone can tell.
            var relayOnly = trusted.Where(p => p.ConnectionPath == ConnectionPath.Relay)
                                   .Select(p => p.Name.IsNotNullOrWhiteSpace() ? p.Name : p.NodeId)
                                   .ToList();

            if (relayOnly.Any())
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.PeerRelayOnly,
                    _localizationService.GetLocalizedString("PeerRelayOnlyMessage", new Dictionary<string, object>
                    {
                        { "peerNames", string.Join(", ", relayOnly) }
                    }));
            }

            // A direct connection to another node is proof that this network lets one through,
            // whatever the transport's own look at the network concluded.
            if (trusted.Any(p => p.ConnectionPath == ConnectionPath.Direct))
            {
                return new HealthCheck(GetType());
            }

            // "False" means the transport could not see UDP getting out. That is what a firewall
            // looks like, and also what a relay that offers no address discovery looks like.
            if (network.Udp == false)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Notice,
                    HealthCheckReason.PeerNetworkNoUdp,
                    _localizationService.GetLocalizedString("PeerNetworkNoUdpMessage"));
            }

            if (network.MappingVaries == true)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Notice,
                    HealthCheckReason.PeerNetworkVaryingPort,
                    _localizationService.GetLocalizedString("PeerNetworkVaryingPortMessage"));
            }

            return new HealthCheck(GetType());
        }

        // The transport takes a few seconds after it starts to look at the network.
        public override bool CheckOnStartup => false;
    }
}

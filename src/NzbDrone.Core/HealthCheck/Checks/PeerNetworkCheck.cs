using System.Linq;
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

            // A direct connection to another node is proof that this network lets one through,
            // whatever the transport's own look at the network concluded.
            if (_peers.All().Any(p => p.State == PeerState.Trusted && p.ConnectionPath == ConnectionPath.Direct))
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

using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class PeerNetworkCheckFixture : CoreTest<PeerNetworkCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Warning Message");

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Returns((string key, Dictionary<string, object> tokens) => $"{key}: {tokens["peerNames"]}");

            GivenPeers();
        }

        private void GivenPeers(params Peer[] peers)
        {
            Mocker.GetMock<IPeerRepository>().Setup(r => r.All()).Returns(new List<Peer>(peers));
        }

        private void GivenNetwork(bool? udp, bool? mappingVaries)
        {
            Mocker.GetMock<IPeerTransport>()
                  .Setup(t => t.GetNetworkStatus())
                  .Returns(new NetworkStatus { Udp = udp, MappingVaries = mappingVaries });
        }

        [Test]
        public void should_be_ok_on_a_network_that_lets_nodes_connect_directly()
        {
            GivenNetwork(true, false);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_be_ok_while_nothing_is_known_yet()
        {
            GivenNetwork(null, null);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_give_notice_when_udp_was_not_seen_getting_out()
        {
            GivenNetwork(false, null);

            Subject.Check().ShouldBeNotice();
        }

        [Test]
        public void should_be_ok_once_a_direct_connection_has_proved_the_network_works()
        {
            GivenNetwork(false, true);
            GivenPeers(new Peer { State = PeerState.Trusted, ConnectionPath = ConnectionPath.Direct });

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_not_be_reassured_by_a_node_that_is_unchecked_or_no_longer_trusted()
        {
            GivenNetwork(false, null);
            GivenPeers(
                new Peer { State = PeerState.Trusted, ConnectionPath = ConnectionPath.Unknown },
                new Peer { State = PeerState.Revoked, ConnectionPath = ConnectionPath.Direct });

            Subject.Check().ShouldBeNotice();
        }

        [Test]
        public void should_warn_and_name_the_node_when_it_was_reached_only_through_a_relay()
        {
            GivenNetwork(true, false);
            GivenPeers(new Peer { NodeId = "bob", Name = "Bob", State = PeerState.Trusted, ConnectionPath = ConnectionPath.Relay });

            Subject.Check().ShouldBeWarning("PeerRelayOnlyMessage: Bob");
        }

        [Test]
        public void should_still_warn_about_a_relayed_node_when_another_is_reached_directly()
        {
            GivenNetwork(true, false);
            GivenPeers(
                new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted, ConnectionPath = ConnectionPath.Direct },
                new Peer { NodeId = "bob", Name = "Bob", State = PeerState.Trusted, ConnectionPath = ConnectionPath.Relay },
                new Peer { NodeId = "carol", State = PeerState.Trusted, ConnectionPath = ConnectionPath.Relay });

            Subject.Check().ShouldBeWarning("PeerRelayOnlyMessage: Bob, carol");
        }

        [Test]
        public void should_not_warn_about_a_relayed_node_that_is_no_longer_trusted()
        {
            GivenNetwork(true, false);
            GivenPeers(new Peer { NodeId = "bob", Name = "Bob", State = PeerState.Revoked, ConnectionPath = ConnectionPath.Relay });

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_give_notice_when_the_public_port_changes_with_the_destination()
        {
            GivenNetwork(true, true);

            Subject.Check().ShouldBeNotice();
        }

        [Test]
        public void should_be_ok_when_the_transport_is_not_running_because_another_check_says_so()
        {
            Mocker.GetMock<IPeerTransport>()
                  .Setup(t => t.GetNetworkStatus())
                  .Throws(new TransportException(TransportFailure.Unavailable, "not running"));

            Subject.Check().ShouldBeOk();
        }
    }
}

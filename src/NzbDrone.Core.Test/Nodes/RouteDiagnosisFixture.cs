using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Nodes
{
    // What a transfer that found no direct route says about the route.
    [TestFixture]
    public class RouteDiagnosisFixture : CoreTest<RouteDiagnosis>
    {
        private Peer _john;

        [SetUp]
        public void Setup()
        {
            _john = new Peer { NodeId = "john", Name = "John", State = PeerState.Trusted };

            GivenNetwork(true, false);
        }

        private void GivenNetwork(bool? udp, bool? mappingVaries)
        {
            Mocker.GetMock<IPeerTransport>()
                  .Setup(t => t.GetNetworkStatus())
                  .Returns(new NetworkStatus { Udp = udp, MappingVaries = mappingVaries });
        }

        private void GivenChecked(ConnectionPath path, TimeSpan ago)
        {
            _john.ConnectionPath = path;
            _john.ConnectionChecked = DateTime.UtcNow - ago;
        }

        [Test]
        public void it_should_say_when_no_check_has_been_made()
        {
            Subject.Describe(_john).Should().StartWith("No connection check has been made with John.");
        }

        [Test]
        public void it_should_say_that_the_last_check_found_only_a_relay()
        {
            GivenChecked(ConnectionPath.Relay, TimeSpan.FromSeconds(20));

            Subject.Describe(_john).Should().StartWith("A connection check just before this reached John only through a relay.");
        }

        [Test]
        public void it_should_say_that_the_route_comes_and_goes_when_the_last_check_was_direct()
        {
            GivenChecked(ConnectionPath.Direct, TimeSpan.FromMinutes(40));

            Subject.Describe(_john).Should().StartWith("A connection check 40 minutes before this found a direct route to John, so the route comes and goes.");
        }

        [Test]
        public void it_should_say_that_the_node_could_not_be_reached_at_all()
        {
            GivenChecked(ConnectionPath.Unknown, TimeSpan.FromHours(5));

            Subject.Describe(_john).Should().StartWith("A connection check 5 hours before this could not reach John at all.");
        }

        [Test]
        public void an_old_check_should_be_dated_in_days()
        {
            GivenChecked(ConnectionPath.Relay, TimeSpan.FromDays(3.5));

            Subject.Describe(_john).Should().Contain("3 days before this");
        }

        [Test]
        public void a_network_with_a_port_for_every_destination_should_point_at_this_nodes_router()
        {
            GivenNetwork(true, true);

            Subject.Describe(_john).Should().Contain("forwarding the UDP port on this node's router");
        }

        [Test]
        public void udp_not_seen_getting_out_should_point_at_this_machines_firewall()
        {
            GivenNetwork(false, null);

            Subject.Describe(_john).Should().Contain("start with the firewall on this machine");
        }

        [Test]
        public void a_network_that_looks_fine_should_point_at_both_firewalls_and_then_the_other_side()
        {
            Subject.Describe(_john).Should().Contain("check the firewall on both machines").And.Contain("the router on John's side");
        }

        [Test]
        public void with_nothing_known_about_this_side_it_should_still_say_where_to_look()
        {
            Mocker.GetMock<IPeerTransport>()
                  .Setup(t => t.GetNetworkStatus())
                  .Throws(new TransportException(TransportFailure.Unavailable, "not running"));

            Subject.Describe(_john).Should().EndWith("Check the firewall on both machines for incoming UDP, then forward the UDP port on either router.");
        }

        [Test]
        public void a_node_with_no_name_should_still_read_as_a_sentence()
        {
            _john.Name = null;
            GivenChecked(ConnectionPath.Relay, TimeSpan.FromSeconds(5));

            Subject.Describe(_john).Should().StartWith("A connection check just before this reached the other node only through a relay.");
        }
    }
}

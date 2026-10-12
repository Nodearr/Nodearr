using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Nodes
{
    // When a connection is checked without anyone asking.
    [TestFixture]
    public class PeerConnectionUpkeepFixture : CoreTest<PeerConnectionUpkeep>
    {
        private Peer _alice;
        private Peer _bob;
        private Peer _carol;

        [SetUp]
        public void Setup()
        {
            _alice = new Peer { Id = 1, NodeId = "alice", Name = "Alice", State = PeerState.Trusted };
            _bob = new Peer { Id = 2, NodeId = "bob", Name = "Bob", State = PeerState.Trusted };
            _carol = new Peer { Id = 3, NodeId = "carol", Name = "Carol", State = PeerState.Revoked };

            Mocker.GetMock<IPeerRepository>().Setup(r => r.All()).Returns(new List<Peer> { _alice, _bob, _carol });
            Mocker.GetMock<IPeerRepository>().Setup(r => r.Find(1)).Returns(_alice);
            Mocker.GetMock<IPeerRepository>().Setup(r => r.Find(2)).Returns(_bob);
            Mocker.GetMock<IPeerRepository>().Setup(r => r.Find(3)).Returns(_carol);
            Mocker.GetMock<IPeerTransport>().SetupGet(t => t.IsRunning).Returns(true);

            Subject.PairingSettle = TimeSpan.Zero;
        }

        private void ShouldHaveChecked(Peer peer, Times times)
        {
            Mocker.GetMock<IPeerService>().Verify(s => s.Ping(peer.Id), times);
        }

        [Test]
        public void the_schedule_should_check_every_trusted_node_and_no_other()
        {
            Subject.Execute(new CheckPeerConnectionsCommand());

            ShouldHaveChecked(_alice, Times.Once());
            ShouldHaveChecked(_bob, Times.Once());
            ShouldHaveChecked(_carol, Times.Never());
        }

        [Test]
        public void nothing_should_be_checked_while_the_transport_is_not_running()
        {
            Mocker.GetMock<IPeerTransport>().SetupGet(t => t.IsRunning).Returns(false);

            Subject.Execute(new CheckPeerConnectionsCommand());
            Subject.HandleAsync(new PeerStateChangedEvent(_alice, PeerState.Pending));
            Subject.HandleAsync(new TransferAttemptEndedEvent(_alice.Id, TransferFailure.NoDirectRoute));

            ShouldHaveChecked(_alice, Times.Never());
        }

        [Test]
        public void one_node_failing_should_not_stop_the_rest_being_checked()
        {
            Mocker.GetMock<IPeerService>().Setup(s => s.Ping(_alice.Id)).Throws(new InvalidOperationException("boom"));

            Subject.Execute(new CheckPeerConnectionsCommand());

            ShouldHaveChecked(_bob, Times.Once());
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void a_node_that_has_just_been_paired_should_be_checked()
        {
            Subject.HandleAsync(new PeerStateChangedEvent(_alice, PeerState.Pending));

            ShouldHaveChecked(_alice, Times.Once());
        }

        [Test]
        public void a_node_whose_trust_just_ended_should_not_be_checked()
        {
            Subject.HandleAsync(new PeerStateChangedEvent(_carol, PeerState.Trusted));

            ShouldHaveChecked(_carol, Times.Never());
        }

        [TestCase(TransferFailure.NoDirectRoute)]
        [TestCase(TransferFailure.DirectPathLost)]
        [TestCase(TransferFailure.PeerUnreachable)]
        public void a_transfer_that_found_no_route_should_have_the_connection_checked(TransferFailure failure)
        {
            _alice.ConnectionPath = ConnectionPath.Direct;
            _alice.ConnectionChecked = DateTime.UtcNow.AddHours(-3);

            Subject.HandleAsync(new TransferAttemptEndedEvent(_alice.Id, failure));

            ShouldHaveChecked(_alice, Times.Once());
        }

        [Test]
        public void a_transfer_that_keeps_failing_should_not_have_it_checked_every_time()
        {
            _alice.ConnectionPath = ConnectionPath.Relay;
            _alice.ConnectionChecked = DateTime.UtcNow.AddSeconds(-20);

            Subject.HandleAsync(new TransferAttemptEndedEvent(_alice.Id, TransferFailure.NoDirectRoute));

            ShouldHaveChecked(_alice, Times.Never());
        }

        [TestCase(TransferFailure.VerificationFailed)]
        [TestCase(TransferFailure.NotAuthorized)]
        [TestCase(TransferFailure.DiskError)]
        public void a_failure_that_says_nothing_about_the_route_should_not_cause_a_check(TransferFailure failure)
        {
            Subject.HandleAsync(new TransferAttemptEndedEvent(_alice.Id, failure));

            ShouldHaveChecked(_alice, Times.Never());
        }

        [TestCase(ConnectionPath.Relay)]
        [TestCase(ConnectionPath.Unknown)]
        public void a_file_that_arrived_should_correct_a_check_that_said_otherwise(ConnectionPath lastKnown)
        {
            _alice.ConnectionPath = lastKnown;
            _alice.ConnectionChecked = DateTime.UtcNow.AddSeconds(-5);

            Subject.HandleAsync(new TransferAttemptEndedEvent(_alice.Id, TransferFailure.None));

            ShouldHaveChecked(_alice, Times.Once());
        }

        [Test]
        public void a_file_that_arrived_over_a_route_already_known_to_be_direct_needs_no_check()
        {
            _alice.ConnectionPath = ConnectionPath.Direct;

            Subject.HandleAsync(new TransferAttemptEndedEvent(_alice.Id, TransferFailure.None));

            ShouldHaveChecked(_alice, Times.Never());
        }

        [Test]
        public void a_node_that_stopped_being_trusted_meanwhile_should_be_left_alone()
        {
            Mocker.GetMock<IPeerService>().Setup(s => s.Ping(_alice.Id)).Throws(new PeerException("Only a trusted node can be contacted"));

            Assert.DoesNotThrow(() => Subject.HandleAsync(new PeerStateChangedEvent(_alice, PeerState.Pending)));
        }
    }
}

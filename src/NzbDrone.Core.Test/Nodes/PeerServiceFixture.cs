using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Nodes
{
    [TestFixture]
    public class PeerServiceFixture : CoreTest
    {
        private InMemoryNetwork _network;
        private TestNode _alice;
        private TestNode _bob;
        private TestNode _carol;

        [SetUp]
        public void Setup()
        {
            _network = new InMemoryNetwork();
            _alice = new TestNode(_network, "Alice", TestLogger);
            _bob = new TestNode(_network, "Bob", TestLogger);
            _carol = new TestNode(_network, "Carol", TestLogger);
        }

        private void GivenPaired(TestNode inviter, TestNode invited)
        {
            invited.Service.RedeemInvite(inviter.Service.CreateInvite(TimeSpan.FromHours(1), null).Code);
        }

        [Test]
        public void pairing_should_make_both_nodes_trust_each_other()
        {
            GivenPaired(_alice, _bob);

            _alice.PeerFor(_bob).Should().Match<Peer>(p => p.State == PeerState.Trusted && p.Name == "Bob");
            _bob.PeerFor(_alice).Should().Match<Peer>(p => p.State == PeerState.Trusted && p.Name == "Alice");
        }

        [Test]
        public void pairing_should_record_where_to_reach_the_other_node()
        {
            GivenPaired(_alice, _bob);

            _alice.PeerFor(_bob).ToAddress().IpAddresses.Should().NotBeEmpty();
            _bob.PeerFor(_alice).RelayUrl.Should().Be("memory://relay");
        }

        [Test]
        public void an_invite_should_work_only_once()
        {
            var code = _alice.Service.CreateInvite(TimeSpan.FromHours(1), null).Code;

            _bob.Service.RedeemInvite(code);

            Assert.Throws<PeerException>(() => _carol.Service.RedeemInvite(code));
            _alice.PeerFor(_carol).Should().BeNull();
            _carol.PeerFor(_alice).State.Should().Be(PeerState.Pending);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_reused_invite_should_not_cost_a_node_the_trust_it_already_has()
        {
            var code = _alice.Service.CreateInvite(TimeSpan.FromHours(1), null).Code;

            _bob.Service.RedeemInvite(code);

            Assert.Throws<PeerException>(() => _bob.Service.RedeemInvite(code));
            _bob.PeerFor(_alice).Should().Match<Peer>(p => p.State == PeerState.Trusted && p.LastError == null);
            _alice.PeerFor(_bob).State.Should().Be(PeerState.Trusted);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void an_invite_should_stop_working_when_it_expires()
        {
            var created = _alice.Service.CreateInvite(TimeSpan.FromHours(1), null);

            // The code still claims to be valid. The inviting node knows better.
            _alice.Invites.Single().Expires = DateTime.UtcNow.AddMinutes(-1);

            Assert.Throws<PeerException>(() => _bob.Service.RedeemInvite(created.Code));
            _alice.PeerFor(_bob).Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void an_expired_code_should_not_even_be_sent()
        {
            var expired = new InviteCode { NodeId = _alice.NodeId, Secret = "s", ExpiresUnix = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds() };

            Assert.Throws<PeerException>(() => _bob.Service.RedeemInvite(expired.Encode()));
            _alice.Transport.Delivered.Should().Be(0);
        }

        [Test]
        public void a_guessed_secret_should_not_pair()
        {
            _alice.Service.CreateInvite(TimeSpan.FromHours(1), null);

            var forged = new InviteCode { NodeId = _alice.NodeId, Secret = InviteCode.NewSecret(), ExpiresUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() };

            Assert.Throws<PeerException>(() => _bob.Service.RedeemInvite(forged.Encode()));
            _alice.PeerFor(_bob).Should().BeNull();
            _alice.Invites.Single().RedeemedAt.Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_node_should_not_redeem_its_own_invite()
        {
            var code = _alice.Service.CreateInvite(TimeSpan.FromHours(1), null).Code;

            Assert.Throws<PeerException>(() => _alice.Service.RedeemInvite(code));
        }

        [Test]
        public void only_a_hash_of_the_invite_secret_should_be_stored()
        {
            var created = _alice.Service.CreateInvite(TimeSpan.FromHours(1), null);

            InviteCode.TryDecode(created.Code, out var code).Should().BeTrue();
            _alice.Invites.Single().SecretHash.Should().NotContain(code.Secret).And.Be(InviteCode.HashSecret(code.Secret));
        }

        [Test]
        public void a_stranger_should_not_be_answered()
        {
            var ping = new PeerMessage { Type = PeerMessageTypes.Ping }.ToBytes();

            Assert.Throws<TransportException>(() => _carol.Transport.Request(_alice.Address, ping, TimeSpan.FromSeconds(1)))
                  .Failure.Should().Be(TransportFailure.NotAuthorized);
        }

        [Test]
        public void a_stranger_should_not_be_given_the_catalog()
        {
            var request = new PeerMessage { Type = PeerMessageTypes.Catalog }.ToBytes();

            Assert.Throws<TransportException>(() => _carol.Transport.Request(_alice.Address, request, TimeSpan.FromSeconds(1)))
                  .Failure.Should().Be(TransportFailure.NotAuthorized);
            _alice.Offers.Verify(o => o.Answer(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Never());
        }

        [Test]
        public void a_trusted_node_should_be_given_the_catalog_built_for_it()
        {
            GivenPaired(_alice, _bob);

            var reply = _bob.Service.Ask(_bob.PeerFor(_alice), new PeerMessage { Type = PeerMessageTypes.Catalog });

            reply.CatalogVersion.Should().Be("v1");
            _alice.Offers.Verify(o => o.Answer(It.Is<Peer>(p => p.NodeId == _bob.NodeId), It.IsAny<PeerMessage>()), Times.Once());
        }

        [Test]
        public void a_revoked_node_should_not_be_given_the_catalog()
        {
            GivenPaired(_alice, _bob);
            _alice.Service.Revoke(_alice.PeerFor(_bob).Id);

            var request = new PeerMessage { Type = PeerMessageTypes.Catalog }.ToBytes();

            Assert.Throws<TransportException>(() => _bob.Transport.Request(_alice.Address, request, TimeSpan.FromSeconds(1)));
            _alice.Offers.Verify(o => o.Answer(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Never());
        }

        [Test]
        public void a_new_peer_should_be_offered_nothing_unless_the_invite_said_otherwise()
        {
            GivenPaired(_alice, _bob);

            _alice.PeerFor(_bob).ShareAll.Should().BeFalse();
        }

        [Test]
        public void an_invite_can_share_everything_with_whoever_redeems_it()
        {
            _bob.Service.RedeemInvite(_alice.Service.CreateInvite(TimeSpan.FromHours(1), null, true).Code);

            _alice.PeerFor(_bob).ShareAll.Should().BeTrue();
            _bob.PeerFor(_alice).ShareAll.Should().BeFalse();
        }

        [Test]
        public void a_sharing_hint_should_ask_for_the_catalog_to_be_fetched_again()
        {
            GivenPaired(_alice, _bob);

            _bob.Service.Ask(_bob.PeerFor(_alice), new PeerMessage { Type = PeerMessageTypes.CatalogChanged });

            _alice.Events.Verify(e => e.PublishEvent(It.Is<PeerCatalogChangedEvent>(c => c.PeerId == _alice.PeerFor(_bob).Id)), Times.Once());
        }

        [Test]
        public void a_trusted_node_should_answer_a_ping()
        {
            GivenPaired(_alice, _bob);

            var peer = _bob.Service.Ping(_bob.PeerFor(_alice).Id);

            peer.LastError.Should().BeNull();
            peer.LastSeen.Should().NotBeNull();
        }

        [Test]
        public void a_blocked_node_should_be_refused_before_its_message_is_read()
        {
            GivenPaired(_alice, _bob);
            _alice.Service.Block(_alice.PeerFor(_bob).Id);
            var delivered = _alice.Transport.Delivered;

            var peer = _bob.Service.Ping(_bob.PeerFor(_alice).Id);

            peer.LastError.Should().StartWith("Blocked");
            _alice.Transport.Delivered.Should().Be(delivered);
            _alice.PeerFor(_bob).State.Should().Be(PeerState.Blocked);
        }

        [Test]
        public void a_blocked_node_should_be_refused_even_if_the_transport_lets_it_through()
        {
            GivenPaired(_alice, _bob);
            _alice.Service.Block(_alice.PeerFor(_bob).Id);

            _alice.Service.HandleIncoming(_bob.NodeId, new PeerMessage { Type = PeerMessageTypes.Ping }.ToBytes()).Should().BeNull();
        }

        [Test]
        public void a_blocked_node_should_not_pair_again_with_a_new_invite()
        {
            GivenPaired(_alice, _bob);
            _alice.Service.Block(_alice.PeerFor(_bob).Id);

            var code = _alice.Service.CreateInvite(TimeSpan.FromHours(1), null).Code;

            Assert.Throws<PeerException>(() => _bob.Service.RedeemInvite(code));
            _alice.PeerFor(_bob).State.Should().Be(PeerState.Blocked);
        }

        [Test]
        public void blocking_should_tell_the_rest_of_the_application()
        {
            GivenPaired(_alice, _bob);

            _alice.Service.Block(_alice.PeerFor(_bob).Id);

            _alice.Events.Verify(e => e.PublishEvent(It.Is<PeerStateChangedEvent>(c => c.Peer.NodeId == _bob.NodeId && c.Peer.State == PeerState.Blocked && c.PreviousState == PeerState.Trusted)), Times.Once());
        }

        [Test]
        public void unblocking_should_not_restore_trust()
        {
            GivenPaired(_alice, _bob);
            var id = _alice.PeerFor(_bob).Id;

            _alice.Service.Block(id);
            _alice.Service.Unblock(id);

            _alice.PeerFor(_bob).State.Should().Be(PeerState.Revoked);
            _bob.Service.Ping(_bob.PeerFor(_alice).Id).LastError.Should().StartWith("NotAuthorized");
        }

        [Test]
        public void an_unblocked_node_should_be_able_to_pair_again()
        {
            GivenPaired(_alice, _bob);
            var id = _alice.PeerFor(_bob).Id;

            _alice.Service.Block(id);
            _alice.Service.Unblock(id);
            GivenPaired(_alice, _bob);

            _alice.PeerFor(_bob).State.Should().Be(PeerState.Trusted);
            _alice.Peers.Should().HaveCount(1);
        }

        [Test]
        public void revoking_should_end_trust_on_both_sides()
        {
            GivenPaired(_alice, _bob);

            _alice.Service.Revoke(_alice.PeerFor(_bob).Id);

            _alice.PeerFor(_bob).State.Should().Be(PeerState.Revoked);
            _bob.PeerFor(_alice).State.Should().Be(PeerState.Revoked);
            Assert.Throws<PeerException>(() => _bob.Service.Ping(_bob.PeerFor(_alice).Id));
        }

        [Test]
        public void revoking_should_work_when_the_other_node_is_offline()
        {
            GivenPaired(_alice, _bob);
            _bob.Transport.IsRunning = false;

            _alice.Service.Revoke(_alice.PeerFor(_bob).Id);

            _alice.PeerFor(_bob).State.Should().Be(PeerState.Revoked);
        }

        [Test]
        public void a_blocked_node_should_not_be_deletable()
        {
            GivenPaired(_alice, _bob);
            var id = _alice.PeerFor(_bob).Id;

            _alice.Service.Block(id);

            Assert.Throws<PeerException>(() => _alice.Service.Delete(id));
            _alice.PeerFor(_bob).Should().NotBeNull();
        }

        [Test]
        public void an_unreachable_inviter_should_leave_the_peer_pending()
        {
            var code = _alice.Service.CreateInvite(TimeSpan.FromHours(1), null).Code;

            _alice.Transport.IsRunning = false;

            Assert.Throws<PeerException>(() => _bob.Service.RedeemInvite(code));
            _bob.PeerFor(_alice).Should().Match<Peer>(p => p.State == PeerState.Pending && p.LastError != null);
        }

        [Test]
        public void the_block_list_should_be_given_to_the_transport_at_startup()
        {
            GivenPaired(_alice, _bob);
            _alice.Service.Block(_alice.PeerFor(_bob).Id);

            // A restarted transport has forgotten the list.
            _alice.Transport.SetBlocked(Array.Empty<string>());
            _alice.Service.Handle(new ApplicationStartedEvent());

            _bob.Service.Ping(_bob.PeerFor(_alice).Id).LastError.Should().StartWith("Blocked");
        }

        [TestCase("")]
        [TestCase("hello")]
        [TestCase("nodearr-invite:not-base64!")]
        [TestCase("nodearr-invite:e30")]
        public void a_malformed_code_should_be_refused(string code)
        {
            InviteCode.TryDecode(code, out _).Should().BeFalse();
            Assert.Throws<PeerException>(() => _bob.Service.RedeemInvite(code));
        }

        // One node: a peer service wired to in-memory storage and the in-memory network.
        private class TestNode
        {
            public TestNode(InMemoryNetwork network, string name, Logger logger)
            {
                NodeId = (name.ToLowerInvariant() + new string('0', 64))[..64];
                Transport = network.Join(NodeId);

                var peers = new Mock<IPeerRepository>();
                var nextId = 1;

                peers.Setup(r => r.All()).Returns(() => Peers.ToList());
                peers.Setup(r => r.Get(It.IsAny<int>())).Returns<int>(id => Peers.Single(p => p.Id == id));
                peers.Setup(r => r.FindByNodeId(It.IsAny<string>())).Returns<string>(id => Peers.SingleOrDefault(p => p.NodeId == id));
                peers.Setup(r => r.Update(It.IsAny<Peer>())).Returns<Peer>(p => p);
                peers.Setup(r => r.Delete(It.IsAny<int>())).Callback<int>(id => Peers.RemoveAll(p => p.Id == id));
                peers.Setup(r => r.Insert(It.IsAny<Peer>())).Returns<Peer>(p =>
                {
                    p.Id = nextId++;
                    Peers.Add(p);

                    return p;
                });

                var invites = new Mock<IPeerInviteRepository>();

                invites.Setup(r => r.All()).Returns(() => Invites.ToList());
                invites.Setup(r => r.FindBySecretHash(It.IsAny<string>())).Returns<string>(hash => Invites.SingleOrDefault(i => i.SecretHash == hash));
                invites.Setup(r => r.Update(It.IsAny<PeerInvite>())).Returns<PeerInvite>(i => i);
                invites.Setup(r => r.Insert(It.IsAny<PeerInvite>())).Returns<PeerInvite>(i =>
                {
                    i.Id = Invites.Count + 1;
                    Invites.Add(i);

                    return i;
                });

                Offers.Setup(o => o.Answer(It.IsAny<Peer>(), It.IsAny<PeerMessage>())).Returns(new PeerReply { Ok = true, CatalogVersion = "v1" });

                Service = new NamedPeerService(name, peers.Object, invites.Object, Transport, Offers.Object, Events.Object, logger);
                Service.Handle(new ApplicationStartedEvent());
            }

            public string NodeId { get; }
            public InMemoryPeerTransport Transport { get; }
            public PeerService Service { get; }
            public List<Peer> Peers { get; } = new();
            public List<PeerInvite> Invites { get; } = new();
            public Mock<IEventAggregator> Events { get; } = new();
            public Mock<IOfferExchange> Offers { get; } = new();
            public NodeAddress Address => Transport.GetLocalAddress();

            public Peer PeerFor(TestNode other)
            {
                return Peers.SingleOrDefault(p => p.NodeId == other.NodeId);
            }
        }

        private class NamedPeerService : PeerService
        {
            private readonly string _name;

            public NamedPeerService(string name, IPeerRepository peers, IPeerInviteRepository invites, IPeerTransport transport, IOfferExchange offers, IEventAggregator eventAggregator, Logger logger)
                : base(peers, invites, transport, offers, eventAggregator, logger)
            {
                _name = name;
            }

            public override string LocalName => _name;
        }
    }
}

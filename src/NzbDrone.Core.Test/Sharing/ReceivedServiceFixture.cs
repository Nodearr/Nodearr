using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Sharing
{
    // What a friend sends is kept for the person to act on, and their catalog fetched again.
    [TestFixture]
    public class ReceivedServiceFixture : DbTest<ReceivedService, ReceivedTitle>
    {
        private Peer _alice;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IReceivedTitleRepository>(Mocker.Resolve<ReceivedTitleRepository>());
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });
        }

        private static SentMessage Pingu(string sendId = "s1")
        {
            return new SentMessage { SendId = sendId, Title = "Pingu", Summary = "Season 1", MediaKind = MediaKind.Tv, OfferIds = new List<string> { "e-1-501", "e-1-502" } };
        }

        private void Receive(SentMessage sent)
        {
            Subject.Handle(new TitlesSentEvent(_alice, sent));
        }

        [Test]
        public void a_send_should_be_kept_and_the_friends_catalog_fetched_again()
        {
            Receive(Pingu());

            Subject.Waiting().Should().ContainSingle()
                   .Which.Should().Match<ReceivedTitle>(r => r.PeerId == _alice.Id && r.Title == "Pingu" && r.Summary == "Season 1" && r.OfferIds == "e-1-501,e-1-502" && r.Handled == null);

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<PeerCatalogChangedEvent>(c => c.PeerId == _alice.Id)), Times.Once());
        }

        [Test]
        public void the_same_send_told_twice_should_be_kept_once()
        {
            Receive(Pingu());
            Receive(Pingu());

            Subject.Waiting().Should().ContainSingle();
        }

        [Test]
        public void text_and_names_that_would_not_do_should_be_dropped()
        {
            var sent = Pingu();

            sent.Title = "Pin\u0007gu";
            sent.Summary = new string('x', 301);
            sent.OfferIds = new List<string> { "e-1-501", "../../etc", "t-3-701", null, "e-1-501" };

            Receive(sent);

            Subject.Waiting().Single().Should().Match<ReceivedTitle>(r => r.Title == "Something" && r.Summary == null && r.OfferIds == "e-1-501");
        }

        [TestCase("")]
        [TestCase("../x")]
        public void a_send_with_a_name_that_would_not_do_should_be_ignored(string sendId)
        {
            Receive(Pingu(sendId));

            Subject.Waiting().Should().BeEmpty();
        }

        [Test]
        public void what_was_put_away_should_not_be_waiting()
        {
            Receive(Pingu());

            Subject.Dismiss(Subject.Waiting().Single().Id);

            Subject.Waiting().Should().BeEmpty();
        }

        [Test]
        public void what_a_node_no_longer_trusted_sent_should_not_be_waiting()
        {
            Receive(Pingu());

            var alice = Mocker.Resolve<IPeerRepository>().Get(_alice.Id);

            alice.State = PeerState.Revoked;
            Mocker.Resolve<IPeerRepository>().Update(alice);

            Subject.Waiting().Should().BeEmpty();
        }

        [Test]
        public void more_than_anyone_would_read_should_not_be_kept()
        {
            foreach (var i in Enumerable.Range(0, ReceivedService.MaxWaitingPerPeer + 1))
            {
                Receive(Pingu($"s{i}"));
            }

            Subject.Waiting().Should().HaveCount(ReceivedService.MaxWaitingPerPeer);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void deleting_the_friend_should_delete_what_they_sent()
        {
            Receive(Pingu());

            Subject.Handle(new PeerDeletedEvent(_alice.Id));

            Mocker.Resolve<IReceivedTitleRepository>().All().Should().BeEmpty();
        }
    }
}

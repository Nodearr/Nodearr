using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
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

        [Test]
        public void what_the_arr_apps_know_it_by_should_be_kept_when_it_would_do()
        {
            var sent = Pingu();

            (sent.TvdbId, sent.Year, sent.SeriesType, sent.ImdbId) = (75669, 1989, "anime", "tt0094518");
            sent.Parts = new SentParts { Seasons = new List<int> { 1, -1 }, Episodes = new List<SeasonEpisode> { new() { SeasonNumber = 2, EpisodeNumber = 3 }, null } };

            Receive(sent);

            var received = Subject.Waiting().Single();

            received.Should().Match<ReceivedTitle>(r => r.TvdbId == 75669 && r.Year == 1989 && r.SeriesType == "anime" && r.ImdbId == "tt0094518");
            received.GetParts().Seasons.Should().Equal(1);
            received.GetParts().Episodes.Should().ContainSingle().Which.EpisodeNumber.Should().Be(3);
        }

        [Test]
        public void ids_and_parts_that_would_not_do_should_be_dropped()
        {
            var sent = Pingu();

            (sent.ImdbId, sent.ForeignArtistId, sent.SeriesType, sent.IsAdult) = ("javascript:", "not-a-guid", "weird", true);
            sent.Parts = new SentParts { Albums = new List<string> { "../x", "2869C510-2679-4A3F-BF15-E5E8B49F2F28" } };

            Receive(sent);

            var received = Subject.Waiting().Single();

            received.Should().Match<ReceivedTitle>(r => r.ImdbId == null && r.ForeignArtistId == null && r.SeriesType == null && !r.IsAdult);
            received.GetParts().Albums.Should().Equal("2869c510-2679-4a3f-bf15-e5e8b49f2f28");
        }

        [Test]
        public void whisparrs_ids_should_be_kept_for_adult_content_only()
        {
            var scene = Pingu("scene");

            (scene.MediaKind, scene.IsAdult, scene.StashId, scene.TpdbId, scene.ForeignId) = (MediaKind.Movie, true, "0d7f9e46-1c43-4d5a-9c3e-2b1d6f0a8e11", "<script>", "whisparr2:4411");
            Receive(scene);

            var series = Pingu("series");

            series.StashId = "0d7f9e46-1c43-4d5a-9c3e-2b1d6f0a8e11";
            Receive(series);

            var waiting = Subject.Waiting();

            waiting.Single(r => r.SendId == "scene").Should().Match<ReceivedTitle>(r => r.StashId == "0d7f9e46-1c43-4d5a-9c3e-2b1d6f0a8e11" && r.TpdbId == null && r.ForeignId == "whisparr2:4411");
            waiting.Single(r => r.SendId == "series").StashId.Should().BeNull();
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

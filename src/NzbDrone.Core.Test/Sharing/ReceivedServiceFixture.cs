using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
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
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());

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

        private ReceivedTitle Done(DateTime? completedAt = null)
        {
            Receive(Pingu());
            var received = Mocker.Resolve<IReceivedTitleRepository>().All().Single();

            received.GetItState = GetItState.Done;
            received.GetItCompletedAt = completedAt ?? DateTime.UtcNow;
            Mocker.Resolve<IReceivedTitleRepository>().Update(received);

            return received;
        }

        private void GivenSentEpisodes(bool haveSecond = true, int localResolution = 1080)
        {
            var peerItems = Mocker.Resolve<IPeerCatalogRepository>();

            foreach (var episode in new[] { 1, 2 })
            {
                peerItems.Insert(new PeerCatalogItem
                {
                    PeerId = _alice.Id,
                    RemoteId = $"e-1-{500 + episode}",
                    MediaKind = MediaKind.Tv,
                    TvdbId = 75669,
                    SeasonNumber = 1,
                    EpisodeNumbers = episode.ToString(),
                    Resolution = 1080,
                    LocalStatus = PeerItemStatus.Missing
                });
            }

            var holdings = new LocalHoldings(
                new[] { new CatalogSeries { ApplicationId = 1, ArrId = 10, TvdbId = 75669 } },
                new[]
                {
                    new CatalogEpisode { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 101 },
                    new CatalogEpisode { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 2, ArrEpisodeFileId = haveSecond ? 102 : 0 }
                },
                new[]
                {
                    new CatalogEpisodeFile { ApplicationId = 1, ArrId = 101, Resolution = localResolution },
                    new CatalogEpisodeFile { ApplicationId = 1, ArrId = 102, Resolution = localResolution }
                },
                Array.Empty<CatalogMovie>(),
                Array.Empty<CatalogMovieFile>());

            Mocker.GetMock<ILocalHoldingsFactory>().Setup(f => f.Load()).Returns(holdings);
        }

        [Test]
        public void a_done_card_should_leave_when_every_sent_file_is_had_locally()
        {
            var received = Done();
            GivenSentEpisodes();

            Subject.Waiting().Should().BeEmpty();
            Mocker.Resolve<IReceivedTitleRepository>().Get(received.Id).Handled.Should().NotBeNull();
            Subject.Waiting().Should().BeEmpty();
        }

        [Test]
        public void a_partly_imported_send_should_keep_its_card()
        {
            Done();
            GivenSentEpisodes(haveSecond: false);

            Subject.Waiting().Should().ContainSingle();
        }

        [Test]
        public void a_lower_quality_copy_should_keep_the_card_until_the_upgrade_or_timeout()
        {
            Done();
            GivenSentEpisodes(localResolution: 720);

            Subject.Waiting().Should().ContainSingle();
        }

        [Test]
        public void a_missing_offer_should_not_count_as_imported()
        {
            Done();
            GivenSentEpisodes();
            Mocker.Resolve<IPeerCatalogRepository>().DeleteForPeer(_alice.Id, new[] { "e-1-502" });

            Subject.Waiting().Should().ContainSingle();
        }

        [Test]
        public void a_done_card_should_leave_after_a_day_even_without_a_catalog()
        {
            var received = Done(DateTime.UtcNow.AddHours(-25));

            Subject.Waiting().Should().BeEmpty();
            Mocker.Resolve<IReceivedTitleRepository>().Get(received.Id).Handled.Should().NotBeNull();
            Mocker.GetMock<ILocalHoldingsFactory>().Verify(f => f.Load(), Times.Never());
        }

        [Test]
        public void the_day_should_start_at_success_rather_than_when_the_title_was_received()
        {
            var received = Done();

            received.Received = DateTime.UtcNow.AddDays(-7);
            Mocker.Resolve<IReceivedTitleRepository>().Update(received);

            Subject.Waiting().Should().ContainSingle();
        }

        [TestCase(GetItState.None)]
        [TestCase(GetItState.Working)]
        [TestCase(GetItState.Failed)]
        public void an_unsuccessful_card_should_stay_even_after_a_day_and_with_local_files(GetItState state)
        {
            var received = Done(DateTime.UtcNow.AddHours(-25));

            received.GetItState = state;
            Mocker.Resolve<IReceivedTitleRepository>().Update(received);
            GivenSentEpisodes();

            Subject.Waiting().Should().ContainSingle();
            Mocker.Resolve<IReceivedTitleRepository>().Get(received.Id).Handled.Should().BeNull();
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

        [TestCase(1080, true, true, 0, 0)]
        [TestCase(1080, false, false, 1, 0)]
        [TestCase(720, true, false, 0, 2)]
        public void pending_sends_should_compare_the_exact_files_with_current_holdings(int resolution, bool haveSecond, bool alreadyHaveIt, int newFiles, int upgrades)
        {
            Receive(Pingu());
            GivenSentEpisodes(haveSecond, resolution);

            var received = Subject.Waiting().Single();
            received.Availability.AlreadyHaveIt.Should().Be(alreadyHaveIt);
            received.Availability.NewFiles.Should().Be(newFiles);
            received.Availability.UpgradeFiles.Should().Be(upgrades);
            received.AlreadyHeldSeenAt.Should().BeNull();
            received.GetItState.Should().Be(GetItState.None);
        }

        [Test]
        public void a_multi_episode_file_should_be_new_until_every_episode_is_held()
        {
            Receive(Pingu());
            GivenSentEpisodes(haveSecond: false);
            var repository = Mocker.Resolve<IPeerCatalogRepository>();
            var first = repository.ForPeer(_alice.Id).Single(i => i.RemoteId == "e-1-501");
            first.EpisodeNumbers = "1,2";
            repository.Update(first);

            var availability = Subject.Waiting().Single().Availability;
            availability.AlreadyHaveIt.Should().BeFalse();
            availability.NewFiles.Should().Be(2);
        }

        [Test]
        public void unrelated_missing_episodes_should_not_keep_a_held_single_episode_send_actionable()
        {
            var sent = Pingu();
            sent.OfferIds = new List<string> { "e-1-501" };
            Receive(sent);
            GivenSentEpisodes(haveSecond: false);

            var availability = Subject.Waiting().Single().Availability;
            availability.AlreadyHaveIt.Should().BeTrue();
            availability.NewFiles.Should().Be(0);
        }

        [Test]
        public void working_operations_should_not_start_or_expire_a_held_notification()
        {
            var received = Done();
            GivenSentEpisodes();
            received.GetItState = GetItState.Working;
            Mocker.Resolve<IReceivedTitleRepository>().Update(received);

            Subject.MarkSeen(received.Id).Should().BeTrue();
            Subject.Waiting().Single().AlreadyHeldSeenAt.Should().BeNull();
            received.AlreadyHeldSeenAt = DateTime.UtcNow.AddDays(-2);
            Mocker.Resolve<IReceivedTitleRepository>().Update(received);
            Subject.Waiting().Should().ContainSingle();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void incomplete_or_wrong_kind_offers_should_not_prove_a_send_is_held(bool withdrawn)
        {
            Receive(Pingu());
            GivenSentEpisodes();
            var repository = Mocker.Resolve<IPeerCatalogRepository>();
            if (withdrawn)
            {
                repository.DeleteForPeer(_alice.Id, new[] { "e-1-502" });
            }
            else
            {
                var item = repository.ForPeer(_alice.Id).First();
                item.MediaKind = MediaKind.Movie;
                repository.Update(item);
            }

            var received = Subject.Waiting().Single();
            received.Availability.AlreadyHaveIt.Should().BeFalse();
            received.Availability.NewFiles.Should().BeNull();
            Subject.MarkSeen(received.Id).Should().BeTrue();
            Mocker.Resolve<IReceivedTitleRepository>().Get(received.Id).AlreadyHeldSeenAt.Should().BeNull();
        }

        [TestCase(false, true, true, 1, false, false, 0)]
        [TestCase(true, true, true, 1, false, true, 0)]
        [TestCase(true, true, true, 2, false, false, 1)]
        [TestCase(true, true, true, 2, true, false, 1)]
        [TestCase(true, false, true, 2, false, false, 1)]
        [TestCase(true, true, false, 2, false, true, 0)]
        public void movie_sends_should_honor_profile_and_revision_rules(bool haveFile, bool upgradesAllowed, bool upgradeToPropers, int revision, bool repack, bool alreadyHaveIt, int upgrades)
        {
            Receive(new SentMessage { SendId = "movie", Title = "Nosferatu", MediaKind = MediaKind.Movie, OfferIds = new List<string> { "m-2-701" } });
            Mocker.Resolve<IPeerCatalogRepository>().Insert(new PeerCatalogItem
            {
                PeerId = _alice.Id, RemoteId = "m-2-701", MediaKind = MediaKind.Movie, TmdbId = 653,
                Quality = "WEBDL-1080p", Resolution = 1080, QualitySource = "web", RevisionVersion = revision, RevisionIsRepack = repack
            });
            var profile = new ArrQualityProfileResource
            {
                Id = 3, UpgradeAllowed = upgradesAllowed, Cutoff = 3,
                Items = new List<ArrQualityProfileItemResource>
                {
                    new() { Allowed = true, Quality = new ArrQualityResource { Id = 3, Name = "WEBDL-1080p" } }
                }
            }.ToCatalog(upgradeToPropers);
            profile.ApplicationId = 2;
            Mocker.GetMock<ILocalHoldingsFactory>().Setup(f => f.Load()).Returns(new LocalHoldings(
                Array.Empty<CatalogSeries>(),
                Array.Empty<CatalogEpisode>(),
                Array.Empty<CatalogEpisodeFile>(),
                new[] { new CatalogMovie { ApplicationId = 2, ArrId = 20, TmdbId = 653, QualityProfileId = 3, ArrMovieFileId = haveFile ? 701 : 0 } },
                new[] { new CatalogMovieFile { ApplicationId = 2, ArrId = 701, Quality = "WEBDL-1080p", Resolution = 1080, QualitySource = "web" } },
                new[] { profile }));

            var availability = Subject.Waiting().Single().Availability;
            availability.AlreadyHaveIt.Should().Be(alreadyHaveIt);
            availability.UpgradeFiles.Should().Be(upgrades);
            availability.NewFiles.Should().Be(haveFile ? 0 : 1);
        }

        [Test]
        public void polling_an_old_unseen_card_should_not_start_or_expire_its_day()
        {
            Receive(Pingu());
            GivenSentEpisodes();
            var repository = Mocker.Resolve<IReceivedTitleRepository>();
            var received = repository.All().Single();
            received.Received = DateTime.UtcNow.AddDays(-7);
            repository.Update(received);

            Subject.Waiting().Single().AlreadyHeldSeenAt.Should().BeNull();
            Subject.Waiting().Single().AlreadyHeldSeenAt.Should().BeNull();
            repository.Get(received.Id).Handled.Should().BeNull();
        }

        [Test]
        public void marking_a_held_card_seen_should_persist_only_the_first_display_time()
        {
            Receive(Pingu());
            GivenSentEpisodes();
            var repository = Mocker.Resolve<IReceivedTitleRepository>();
            var id = repository.All().Single().Id;
            Subject.MarkSeen(id).Should().BeTrue();
            repository.Get(id).AlreadyHeldSeenAt.Should().NotBeNull();

            var received = repository.Get(id);
            received.AlreadyHeldSeenAt = DateTime.UtcNow.AddHours(-23);
            repository.Update(received);
            var firstSeen = repository.Get(id).AlreadyHeldSeenAt;

            Subject.MarkSeen(id).Should().BeTrue();
            Subject.Waiting().Single().AlreadyHeldSeenAt.Should().Be(firstSeen);
            repository.Get(id).GetItState.Should().Be(GetItState.None);
        }

        [TestCase(-23, false)]
        [TestCase(-25, true)]
        public void held_notifications_should_leave_a_day_after_first_display(int hours, bool expired)
        {
            Receive(Pingu());
            GivenSentEpisodes();
            var repository = Mocker.Resolve<IReceivedTitleRepository>();
            var received = repository.All().Single();
            received.AlreadyHeldSeenAt = DateTime.UtcNow.AddHours(hours);
            repository.Update(received);

            Subject.Waiting().Should().HaveCount(expired ? 0 : 1);
            repository.Get(received.Id).Handled.HasValue.Should().Be(expired);
        }

        [TestCase(false, 1080)]
        [TestCase(true, 720)]
        public void a_previously_seen_card_should_become_actionable_if_holdings_change(bool haveSecond, int resolution)
        {
            Receive(Pingu());
            GivenSentEpisodes(haveSecond, resolution);
            var repository = Mocker.Resolve<IReceivedTitleRepository>();
            var received = repository.All().Single();
            received.AlreadyHeldSeenAt = DateTime.UtcNow.AddDays(-2);
            repository.Update(received);

            var waiting = Subject.Waiting().Single();
            waiting.Availability.AlreadyHaveIt.Should().BeFalse();
            waiting.AlreadyHeldSeenAt.Should().BeNull();
            Subject.MarkSeen(received.Id).Should().BeTrue();
            repository.Get(received.Id).AlreadyHeldSeenAt.Should().BeNull();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void dismissed_or_untrusted_cards_cannot_be_marked_seen(bool dismissed)
        {
            Receive(Pingu());
            GivenSentEpisodes();
            var repository = Mocker.Resolve<IReceivedTitleRepository>();
            var id = repository.All().Single().Id;
            if (dismissed)
            {
                Subject.Dismiss(id);
            }
            else
            {
                _alice.State = PeerState.Revoked;
                Mocker.Resolve<IPeerRepository>().Update(_alice);
            }

            Subject.MarkSeen(id).Should().BeFalse();
            Subject.MarkSeen(999).Should().BeFalse();
            repository.Get(id).AlreadyHeldSeenAt.Should().BeNull();
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

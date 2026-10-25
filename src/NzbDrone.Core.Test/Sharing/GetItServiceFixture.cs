using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Sharing
{
    // Get it adds what a friend sent to the right Arr app, filled in the way most of the library is.
    [TestFixture]
    public class GetItServiceFixture : DbTest<GetItService, ReceivedTitle>
    {
        private Peer _alice;
        private Mock<ILibraryAdder> _sonarr;
        private LibraryAddRequest _asked;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IReceivedTitleRepository>(Mocker.Resolve<ReceivedTitleRepository>());
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());
            Mocker.SetConstant<ICatalogSeriesRepository>(Mocker.Resolve<CatalogSeriesRepository>());
            Mocker.SetConstant<ICatalogMovieRepository>(Mocker.Resolve<CatalogMovieRepository>());
            Mocker.SetConstant<ICatalogArtistRepository>(Mocker.Resolve<CatalogArtistRepository>());

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });

            var sonarr = new ApplicationDefinition { Id = 1, Name = "Sonarr", Implementation = "Sonarr", SyncLevel = ApplicationSyncLevel.FullSync };
            var application = new Mock<IApplication>();

            _sonarr = application.As<ILibraryAdder>();
            _sonarr.Setup(a => a.GetAddChoices()).Returns(new LibraryAddChoices
            {
                RootFolders = new List<LibraryRootFolder> { new() { Path = "/tv", FreeSpace = 100 }, new() { Path = "/anime" } },
                QualityProfiles = new List<LibraryProfile> { new() { Id = 1, Name = "Any" }, new() { Id = 4, Name = "HD-1080p" } }
            });
            _sonarr.Setup(a => a.AddAndSearch(It.IsAny<LibraryAddRequest>()))
                   .Callback<LibraryAddRequest>(r => _asked = r)
                   .Returns("Added to Sonarr, monitoring Season 1, and searching");

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(new List<ApplicationDefinition> { sonarr });
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.Find(1)).Returns(sonarr);
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.Get(1)).Returns(sonarr);
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.GetInstance(sonarr)).Returns(application.Object);

            // Most of the library is in /anime, on HD-1080p.
            var series = Mocker.Resolve<ICatalogSeriesRepository>();

            series.Insert(new CatalogSeries { ApplicationId = 1, ArrId = 1, TvdbId = 1, Title = "One", Path = "/tv/One", QualityProfileId = 1 });
            series.Insert(new CatalogSeries { ApplicationId = 1, ArrId = 2, TvdbId = 2, Title = "Two", Path = "/anime/Two", QualityProfileId = 4 });
            series.Insert(new CatalogSeries { ApplicationId = 1, ArrId = 3, TvdbId = 3, Title = "Three", Path = "/anime/Three", QualityProfileId = 4 });
        }

        // Another app beside the Sonarr every test has.
        private Mock<ILibraryAdder> GivenApp(ApplicationDefinition definition)
        {
            var application = new Mock<IApplication>();
            var adder = application.As<ILibraryAdder>();

            adder.Setup(a => a.GetAddChoices()).Returns(new LibraryAddChoices
            {
                RootFolders = new List<LibraryRootFolder> { new() { Path = "/adult" } },
                QualityProfiles = new List<LibraryProfile> { new() { Id = 1, Name = "Any" } }
            });
            adder.Setup(a => a.AddAndSearch(It.IsAny<LibraryAddRequest>())).Callback<LibraryAddRequest>(r => _asked = r).Returns("Already in Whisparr, monitored, and searching");

            var all = Mocker.GetMock<IApplicationFactory>().Object.All().Append(definition).ToList();

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(all);
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.Find(definition.Id)).Returns(definition);
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.Get(definition.Id)).Returns(definition);
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.GetInstance(definition)).Returns(application.Object);

            return adder;
        }

        private ReceivedTitle Received(Action<ReceivedTitle> change = null)
        {
            var received = new ReceivedTitle
            {
                PeerId = _alice.Id,
                SendId = Guid.NewGuid().ToString("N"),
                Title = "Pingu",
                MediaKind = MediaKind.Tv,
                TvdbId = 75669,
                SeriesType = "daily",
                OfferIds = "e-1-501",
                Parts = JsonSerializer.Serialize(new SentParts { Seasons = new List<int> { 1 } }),
                Received = DateTime.UtcNow
            };

            change?.Invoke(received);

            return Mocker.Resolve<IReceivedTitleRepository>().Insert(received);
        }

        private ReceivedTitle Reload(ReceivedTitle received) => Mocker.Resolve<IReceivedTitleRepository>().Get(received.Id);

        [Test]
        public void a_series_should_go_to_sonarr_with_what_most_of_the_library_uses()
        {
            var target = Subject.Plan(Received().Id).Targets.Single();

            target.InLibrary.Should().BeFalse();
            target.Problem.Should().BeNull();
            target.Defaults.Should().Match<GetItOptions>(o => o.ApplicationId == 1 && o.RootFolderPath == "/anime" && o.QualityProfileId == 4 && o.SeriesType == "daily" && o.SeasonFolder);
        }

        [Test]
        public void a_series_sonarr_has_should_say_so()
        {
            var target = Subject.Plan(Received(r => r.TvdbId = 2).Id).Targets.Single();

            target.InLibrary.Should().BeTrue();
        }

        [Test]
        public void a_scene_should_go_to_whisparr_v3_and_be_known_there_by_its_stashdb_id()
        {
            var whisparr = GivenApp(new ApplicationDefinition { Id = 5, Name = "Whisparr", Implementation = "WhisparrV3" });

            Mocker.Resolve<ICatalogMovieRepository>().Insert(new CatalogMovie { ApplicationId = 5, ArrId = 50, Title = "A scene", ItemType = "scene", StashId = "0d7f9e46-1c43-4d5a-9c3e-2b1d6f0a8e11", Path = "/adult/A scene" });

            var received = Received(r => (r.MediaKind, r.IsAdult, r.TvdbId, r.Parts, r.StashId) = (MediaKind.Movie, true, 0, null, "0D7F9E46-1C43-4D5A-9C3E-2B1D6F0A8E11"));

            Subject.Plan(received.Id).Targets.Should().ContainSingle().Which.Should().Match<GetItTarget>(t => t.ApplicationId == 5 && t.InLibrary);

            Subject.Execute(new GetItCommand { ReceivedTitleId = received.Id, ApplicationId = 5 });

            _asked.Should().Match<LibraryAddRequest>(r => r.ExistingArrId == 50 && r.StashId == "0D7F9E46-1C43-4D5A-9C3E-2B1D6F0A8E11");
            whisparr.Verify(w => w.AddAndSearch(It.IsAny<LibraryAddRequest>()), Times.Once());
        }

        [Test]
        public void adult_content_should_never_go_to_radarr()
        {
            GivenApp(new ApplicationDefinition { Id = 6, Name = "Radarr", Implementation = "Radarr" });

            Subject.Plan(Received(r => (r.MediaKind, r.IsAdult, r.TmdbId) = (MediaKind.Movie, true, 5)).Id).Problem.Should().Contain("Connect Whisparr v3");
        }

        [Test]
        public void with_only_whisparr_v2_it_should_say_it_cannot_be_added_there_yet()
        {
            GivenApp(new ApplicationDefinition { Id = 7, Name = "Whisparr v2", Implementation = "WhisparrV2" });

            var plan = Subject.Plan(Received(r => (r.MediaKind, r.IsAdult, r.TmdbId) = (MediaKind.Movie, true, 5)).Id);

            plan.Problem.Should().Contain("cannot add to Whisparr v2 yet");
            plan.Targets.Should().BeEmpty();
        }

        [Test]
        public void a_scene_known_only_to_whisparr_v2_should_say_why_it_cannot_be_added()
        {
            GivenApp(new ApplicationDefinition { Id = 5, Name = "Whisparr", Implementation = "WhisparrV3" });

            var received = Received(r => (r.MediaKind, r.IsAdult, r.TvdbId, r.Parts, r.ForeignId) = (MediaKind.Movie, true, 0, null, "whisparr2:4411"));

            Subject.Plan(received.Id).Problem.Should().Contain("comes from Whisparr v2");
        }

        [Test]
        public void with_no_app_of_the_kind_it_should_say_which_to_connect()
        {
            Subject.Plan(Received(r => (r.MediaKind, r.ForeignArtistId) = (MediaKind.Music, "2869c510-2679-4a3f-bf15-e5e8b49f2f28")).Id)
                   .Problem.Should().Contain("Connect Lidarr");
        }

        [Test]
        public void an_app_that_does_not_answer_should_say_so()
        {
            _sonarr.Setup(a => a.GetAddChoices()).Throws(new System.Net.Http.HttpRequestException("refused"));

            Subject.Plan(Received().Id).Targets.Single().Problem.Should().Contain("did not answer");
        }

        [Test]
        public void a_note_without_ids_should_wait_for_the_friends_catalog()
        {
            var received = Received(r => (r.TvdbId, r.Parts) = (0, null));

            Subject.Plan(received.Id).Problem.Should().Contain("catalog");

            Mocker.Resolve<IPeerCatalogRepository>().Insert(new PeerCatalogItem { PeerId = _alice.Id, RemoteId = "e-1-501", MediaKind = MediaKind.Tv, TvdbId = 75669, SeasonNumber = 1, EpisodeNumbers = "4,5" });

            Subject.Plan(received.Id).Problem.Should().BeNull();

            Subject.Execute(new GetItCommand { ReceivedTitleId = received.Id, ApplicationId = 1, RootFolderPath = "/tv", QualityProfileId = 1 });

            _asked.TvdbId.Should().Be(75669);
            _asked.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).Should().Equal((1, 4), (1, 5));
        }

        [Test]
        public void what_the_friend_offers_of_the_series_now_should_be_what_is_searched_for()
        {
            var peerItems = Mocker.Resolve<IPeerCatalogRepository>();

            peerItems.Insert(new PeerCatalogItem { PeerId = _alice.Id, RemoteId = "e-1-501", MediaKind = MediaKind.Tv, TvdbId = 75669, SeasonNumber = 1, EpisodeNumbers = "1" });
            peerItems.Insert(new PeerCatalogItem { PeerId = _alice.Id, RemoteId = "e-1-502", MediaKind = MediaKind.Tv, TvdbId = 75669, SeasonNumber = 2, EpisodeNumbers = "3,4" });
            peerItems.Insert(new PeerCatalogItem { PeerId = _alice.Id, RemoteId = "e-1-900", MediaKind = MediaKind.Tv, TvdbId = 1, SeasonNumber = 1, EpisodeNumbers = "1" });

            Subject.Execute(new GetItCommand { ReceivedTitleId = Received(r => r.IsWhole = true).Id, ApplicationId = 1 });

            _asked.OfferedEpisodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).Should().BeEquivalentTo(new[] { (1, 1), (2, 3), (2, 4) });
        }

        [Test]
        public void get_it_should_be_queued_and_shown_as_working()
        {
            var received = Received();

            Subject.GetIt(received.Id, new GetItOptions { ApplicationId = 1, RootFolderPath = "/tv", QualityProfileId = 4 });

            Reload(received).GetItState.Should().Be(GetItState.Working);
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.Is<GetItCommand>(c => c.ReceivedTitleId == received.Id && c.RootFolderPath == "/tv" && c.QualityProfileId == 4), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());

            Assert.Throws<GetItException>(() => Subject.GetIt(received.Id, new GetItOptions { ApplicationId = 1 }));
        }

        [Test]
        public void get_it_should_add_what_was_sent_with_the_choices_made()
        {
            var received = Received();

            Subject.Execute(new GetItCommand { ReceivedTitleId = received.Id, ApplicationId = 1, RootFolderPath = "/tv", QualityProfileId = 4, SeriesType = "anime" });

            _asked.Should().Match<LibraryAddRequest>(r => r.TvdbId == 75669 && r.ExistingArrId == null && !r.Whole && r.RootFolderPath == "/tv" && r.QualityProfileId == 4 && r.SeriesType == "anime");
            _asked.Seasons.Should().Equal(1);

            Reload(received).Should().Match<ReceivedTitle>(r => r.GetItState == GetItState.Done && r.GetItMessage.StartsWith("Added to Sonarr"));
            Reload(received).GetItCompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Test]
        public void a_series_sonarr_has_should_be_named_by_its_id_there()
        {
            Subject.Execute(new GetItCommand { ReceivedTitleId = Received(r => r.TvdbId = 3).Id, ApplicationId = 1 });

            _asked.ExistingArrId.Should().Be(3);
        }

        [Test]
        public void a_refusal_from_the_app_should_be_shown_and_can_be_tried_again()
        {
            _sonarr.Setup(a => a.AddAndSearch(It.IsAny<LibraryAddRequest>())).Throws(new LibraryAddException("Sonarr could not find it on TheTVDB"));

            var received = Received();

            Subject.Execute(new GetItCommand { ReceivedTitleId = received.Id, ApplicationId = 1 });

            Reload(received).Should().Match<ReceivedTitle>(r => r.GetItState == GetItState.Failed && r.GetItMessage == "Sonarr could not find it on TheTVDB");
            ExceptionVerification.ExpectedWarns(1);

            Subject.GetIt(received.Id, new GetItOptions { ApplicationId = 1, RootFolderPath = "/tv", QualityProfileId = 1 });

            Reload(received).GetItState.Should().Be(GetItState.Working);
        }

        [TestCase(null, 1, "root folder")]
        [TestCase("", 1, "root folder")]
        [TestCase("/removed", 1, "root folder")]
        [TestCase("/tv", 0, "quality profile")]
        [TestCase("/tv", 99, "quality profile")]
        public void choices_the_app_does_not_offer_should_be_refused_before_queueing(string root, int profile, string choice)
        {
            var received = Received();

            var error = Assert.Throws<GetItException>(() => Subject.GetIt(received.Id, new GetItOptions { ApplicationId = 1, RootFolderPath = root, QualityProfileId = profile }));

            error.Message.Should().Contain(choice).And.Contain("Pick a");
            Reload(received).GetItState.Should().Be(GetItState.None);
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<GetItCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void choices_should_be_checked_again_after_a_plan_was_shown()
        {
            var received = Received();
            var options = Subject.Plan(received.Id).Targets.Single().Defaults;

            _sonarr.Setup(a => a.GetAddChoices()).Returns(new LibraryAddChoices());

            Assert.Throws<GetItException>(() => Subject.GetIt(received.Id, options)).Message.Should().Contain("root folder");
            Reload(received).GetItState.Should().Be(GetItState.None);
        }

        [Test]
        public void existing_titles_should_not_need_add_choices()
        {
            var received = Received(r => r.TvdbId = 3);
            _sonarr.Setup(a => a.GetAddChoices()).Returns(new LibraryAddChoices());

            Subject.GetIt(received.Id, new GetItOptions { ApplicationId = 1 });

            Reload(received).GetItState.Should().Be(GetItState.Working);
        }

        [TestCase(null)]
        [TestCase(99)]
        public void lidarr_should_refuse_a_metadata_profile_it_does_not_offer(int? profile)
        {
            var lidarr = GivenApp(new ApplicationDefinition { Id = 8, Name = "Lidarr", Implementation = "Lidarr" });
            lidarr.Setup(a => a.GetAddChoices()).Returns(new LibraryAddChoices
            {
                RootFolders = new List<LibraryRootFolder> { new() { Path = "/music" } },
                QualityProfiles = new List<LibraryProfile> { new() { Id = 1 } },
                MetadataProfiles = new List<LibraryProfile> { new() { Id = 2 } }
            });
            var received = Received(r => (r.MediaKind, r.ForeignArtistId) = (MediaKind.Music, "2869c510-2679-4a3f-bf15-e5e8b49f2f28"));

            Assert.Throws<GetItException>(() => Subject.GetIt(received.Id, new GetItOptions { ApplicationId = 8, RootFolderPath = "/music", QualityProfileId = 1, MetadataProfileId = profile }))
                  .Message.Should().Contain("metadata profile").And.Contain("Pick a");
            Reload(received).GetItState.Should().Be(GetItState.None);
        }

        [Test]
        public void an_app_that_does_not_answer_should_not_queue_the_add()
        {
            _sonarr.Setup(a => a.GetAddChoices()).Throws(new System.Net.Http.HttpRequestException("refused"));
            var received = Received();

            Assert.Throws<GetItException>(() => Subject.GetIt(received.Id, new GetItOptions { ApplicationId = 1, RootFolderPath = "/tv", QualityProfileId = 1 }))
                  .Message.Should().Contain("did not answer");
            Reload(received).GetItState.Should().Be(GetItState.None);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_title_should_only_go_to_an_app_of_its_kind()
        {
            var received = Received(r => (r.MediaKind, r.TmdbId) = (MediaKind.Movie, 10331));

            Assert.Throws<GetItException>(() => Subject.GetIt(received.Id, new GetItOptions { ApplicationId = 1 }));
        }

        [Test]
        public void what_was_being_added_when_nodearr_stopped_should_be_shown_as_not_finished()
        {
            var received = Received(r => r.GetItState = GetItState.Working);

            Subject.Handle(new ApplicationStartedEvent());

            Reload(received).Should().Match<ReceivedTitle>(r => r.GetItState == GetItState.Failed && r.GetItMessage.Contains("Try again"));
        }
    }
}

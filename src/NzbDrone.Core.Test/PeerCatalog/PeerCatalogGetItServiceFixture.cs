using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.PeerCatalog
{
    [TestFixture]
    public class PeerCatalogGetItServiceFixture : DbTest<PeerCatalogGetItService, PeerCatalogItem>
    {
        private Peer _alice;
        private Dictionary<int, Mock<ILibraryAdder>> _adders;
        private Dictionary<int, LibraryAddChoices> _choices;
        private LibraryAddRequest _asked;
        private int _askedApp;

        [SetUp]
        public void Setup()
        {
            _asked = null;
            _askedApp = 0;
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<ICatalogSeriesRepository>(Mocker.Resolve<CatalogSeriesRepository>());
            Mocker.SetConstant<ICatalogEpisodeRepository>(Mocker.Resolve<CatalogEpisodeRepository>());
            Mocker.SetConstant<ICatalogEpisodeFileRepository>(Mocker.Resolve<CatalogEpisodeFileRepository>());
            Mocker.SetConstant<ICatalogMovieRepository>(Mocker.Resolve<CatalogMovieRepository>());
            Mocker.SetConstant<ICatalogMovieFileRepository>(Mocker.Resolve<CatalogMovieFileRepository>());
            Mocker.SetConstant<ICatalogQualityProfileRepository>(Mocker.Resolve<CatalogQualityProfileRepository>());
            Mocker.SetConstant<ICatalogArtistRepository>(Mocker.Resolve<CatalogArtistRepository>());
            Mocker.SetConstant<ICatalogAlbumRepository>(Mocker.Resolve<CatalogAlbumRepository>());
            Mocker.SetConstant<ICatalogTrackFileRepository>(Mocker.Resolve<CatalogTrackFileRepository>());
            Mocker.SetConstant<ILocalHoldingsFactory>(Mocker.Resolve<LocalHoldingsFactory>());
            Mocker.SetConstant<ILibraryGetItService>(Mocker.Resolve<LibraryGetItService>());

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted });
            _adders = new();
            _choices = new();

            var definitions = new List<ApplicationDefinition>();

            foreach (var (id, implementation) in new[] { (1, "Sonarr"), (2, "Radarr"), (3, "Lidarr"), (4, "WhisparrV3") })
            {
                var definition = new ApplicationDefinition { Id = id, Name = implementation, Implementation = implementation };
                var application = new Mock<IApplication>();
                var adder = application.As<ILibraryAdder>();
                var choices = new LibraryAddChoices
                {
                    RootFolders = new List<LibraryRootFolder> { new() { Path = $"/library/{id}" } },
                    QualityProfiles = new List<LibraryProfile> { new() { Id = 1 } },
                    MetadataProfiles = new List<LibraryProfile> { new() { Id = 2 } }
                };

                adder.Setup(a => a.GetAddChoices()).Returns(choices);
                adder.Setup(a => a.AddAndSearch(It.IsAny<LibraryAddRequest>()))
                     .Callback<LibraryAddRequest>(r => (_asked, _askedApp) = (r, id))
                     .Returns($"Added to {implementation}, monitored, and searching");
                Mocker.GetMock<IApplicationFactory>().Setup(f => f.Find(id)).Returns(definition);
                Mocker.GetMock<IApplicationFactory>().Setup(f => f.Get(id)).Returns(definition);
                Mocker.GetMock<IApplicationFactory>().Setup(f => f.GetInstance(definition)).Returns(application.Object);
                definitions.Add(definition);
                _adders[id] = adder;
                _choices[id] = choices;
            }

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(definitions);
            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(q => q.Push(It.IsAny<CatalogGetItCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Returns<CatalogGetItCommand, CommandPriority, CommandTrigger>((c, p, t) => new CommandModel { Id = 99, Name = c.Name, Body = c, Priority = p, Trigger = t });
        }

        private PeerCatalogItem Episode(int number = 1, int season = 1, Peer peer = null)
        {
            return Mocker.Resolve<IPeerCatalogRepository>().Insert(new PeerCatalogItem
            {
                PeerId = (peer ?? _alice).Id,
                RemoteId = $"e-1-{(season * 100) + number}",
                MediaKind = MediaKind.Tv,
                TvdbId = 75669,
                Title = "Pingu",
                SeasonNumber = season,
                EpisodeNumbers = number.ToString(),
                SeriesType = "daily"
            });
        }

        private PeerCatalogItem Movie(bool adult = false)
        {
            return Mocker.Resolve<IPeerCatalogRepository>().Insert(new PeerCatalogItem
            {
                PeerId = _alice.Id,
                RemoteId = adult ? "m-4-1" : "m-2-1",
                MediaKind = MediaKind.Movie,
                Title = adult ? "A scene" : "Night of the Living Dead",
                IsAdult = adult,
                TmdbId = adult ? 0 : 10331,
                StashId = adult ? "0d7f9e46-1c43-4d5a-9c3e-2b1d6f0a8e11" : null,
                Resolution = 1080
            });
        }

        private CatalogGetItCommand Queue(PeerCatalogItem item, CatalogGetItScope scope = CatalogGetItScope.File, int? season = null)
        {
            var options = Subject.Plan(item.Id, scope, season).Plan.Targets.Single().Defaults;

            return (CatalogGetItCommand)Subject.GetIt(item.Id, scope, season, options).Body;
        }

        [Test]
        public void a_movie_should_use_the_same_library_operation_as_a_send_without_making_a_home_card()
        {
            var command = Queue(Movie());

            Subject.Execute(command);

            _askedApp.Should().Be(2);
            _asked.Should().Match<LibraryAddRequest>(r => r.TmdbId == 10331 && r.RootFolderPath == "/library/2" && r.QualityProfileId == 1);
            Mocker.Resolve<ReceivedTitleRepository>().All().Should().BeEmpty();
        }

        [Test]
        public void a_file_should_monitor_its_tvdb_episodes_and_keep_the_friends_series_type()
        {
            var item = Episode();
            item.EpisodeNumbers = "4,5";
            item.SceneSeasonNumber = 9;
            item.SceneEpisodeNumbers = "40,41";
            Mocker.Resolve<IPeerCatalogRepository>().Update(item);
            Episode(6);

            Subject.Execute(Queue(item));

            _askedApp.Should().Be(1);
            _asked.Should().Match<LibraryAddRequest>(r => !r.Whole && r.SeriesType == "daily");
            _asked.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).Should().Equal((1, 4), (1, 5));
            _asked.Seasons.Should().BeEmpty();
        }

        [Test]
        public void a_season_should_monitor_that_season_only()
        {
            var item = Episode();
            Episode(2, season: 2);

            Subject.Execute(Queue(item, CatalogGetItScope.Season, 2));

            _asked.Should().Match<LibraryAddRequest>(r => !r.Whole && r.Episodes.Count == 0);
            _asked.Seasons.Should().Equal(2);
        }

        [Test]
        public void a_whole_series_should_search_offers_from_every_trusted_friend()
        {
            var item = Episode();
            var bob = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "bob", State = PeerState.Trusted });
            var revoked = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "revoked", State = PeerState.Revoked });
            Episode(2, peer: bob);
            Episode(3, peer: revoked);

            Subject.Execute(Queue(item, CatalogGetItScope.Title));

            _asked.Whole.Should().BeTrue();
            _asked.OfferedEpisodes.Select(e => e.EpisodeNumber).Should().BeEquivalentTo(new[] { 1, 2 });
        }

        [Test]
        public void an_album_should_add_its_artist_and_monitor_that_album_only()
        {
            var item = Mocker.Resolve<IPeerCatalogRepository>().Insert(new PeerCatalogItem
            {
                PeerId = _alice.Id,
                RemoteId = "a-3-1",
                MediaKind = MediaKind.Music,
                Artist = "Slint",
                Title = "Spiderland",
                ForeignArtistId = "2869c510-2679-4a3f-bf15-e5e8b49f2f28",
                ForeignAlbumId = "2a9c2bbc-4e06-3f42-b3a8-1c5fabc12345",
                Tracks = JsonSerializer.Serialize(new List<OfferTrack> { new() { Id = "t-3-1", Extension = ".flac", Size = 100 } })
            });

            Subject.Execute(Queue(item, CatalogGetItScope.Title));

            _askedApp.Should().Be(3);
            _asked.Should().Match<LibraryAddRequest>(r => !r.Whole && r.ForeignArtistId == item.ForeignArtistId && r.MetadataProfileId == 2);
            _asked.Albums.Should().Equal(item.ForeignAlbumId);
        }

        [Test]
        public void adult_content_should_go_to_whisparr_and_never_radarr()
        {
            var item = Movie(adult: true);

            Subject.Execute(Queue(item));

            _askedApp.Should().Be(4);
            _asked.StashId.Should().Be(item.StashId);
            _adders[2].Verify(a => a.AddAndSearch(It.IsAny<LibraryAddRequest>()), Times.Never());
        }

        [Test]
        public void choices_should_recover_after_a_root_folder_is_added()
        {
            var item = Movie();
            _choices[2].RootFolders.Clear();

            Subject.Plan(item.Id, CatalogGetItScope.File, null).Plan.Targets.Single().Problem.Should().Contain("has no root folder");
            _choices[2].RootFolders.Add(new LibraryRootFolder { Path = "/movies" });

            var options = Subject.Plan(item.Id, CatalogGetItScope.File, null).Plan.Targets.Single().Defaults;
            options.RootFolderPath.Should().Be("/movies");
            Subject.GetIt(item.Id, CatalogGetItScope.File, null, options).Should().NotBeNull();
        }

        [Test]
        public void a_root_folder_removed_since_the_plan_was_shown_should_not_be_queued()
        {
            var item = Movie();
            var options = Subject.Plan(item.Id, CatalogGetItScope.File, null).Plan.Targets.Single().Defaults;
            _choices[2].RootFolders.Clear();

            Assert.Throws<GetItException>(() => Subject.GetIt(item.Id, CatalogGetItScope.File, null, options)).Message.Should().Contain("Pick a root folder again");
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<CatalogGetItCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void an_app_that_does_not_answer_should_give_the_same_plan_problem_as_home()
        {
            _adders[2].Setup(a => a.GetAddChoices()).Throws(new System.Net.Http.HttpRequestException("refused"));

            Subject.Plan(Movie().Id, CatalogGetItScope.File, null).Plan.Targets.Single().Problem.Should().Contain("did not answer");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void trust_and_availability_should_be_checked_again_when_the_command_runs(bool revoke)
        {
            var item = Movie();
            var command = Queue(item);

            if (revoke)
            {
                _alice.State = PeerState.Revoked;
                Mocker.Resolve<IPeerRepository>().Update(_alice);
            }
            else
            {
                Mocker.Resolve<IPeerCatalogRepository>().Delete(item.Id);
            }

            Assert.Throws<CommandFailedException>(() => Subject.Execute(command));
            _asked.Should().BeNull();
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(720, true)]
        [TestCase(1080, false)]
        public void current_holdings_should_allow_an_upgrade_but_refuse_what_is_had(int resolution, bool canGet)
        {
            var item = Movie();
            Mocker.Resolve<ICatalogMovieRepository>().Insert(new CatalogMovie { ApplicationId = 2, ArrId = 10, TmdbId = item.TmdbId, ArrMovieFileId = 50 });
            Mocker.Resolve<ICatalogMovieFileRepository>().Insert(new CatalogMovieFile { ApplicationId = 2, ArrId = 50, Resolution = resolution });

            if (canGet)
            {
                Subject.Execute(Queue(item));
                _asked.ExistingArrId.Should().Be(10);
            }
            else
            {
                Subject.Plan(item.Id, CatalogGetItScope.File, null).Plan.Problem.Should().Contain("already have");
                Assert.Throws<GetItException>(() => Subject.GetIt(item.Id, CatalogGetItScope.File, null, new GetItOptions { ApplicationId = 2 }));
            }
        }

        [Test]
        public void a_whisparr_v2_scene_should_explain_why_it_cannot_be_added()
        {
            var item = Movie(adult: true);
            item.StashId = null;
            item.ForeignId = "whisparr2:4411";
            Mocker.Resolve<IPeerCatalogRepository>().Update(item);

            Subject.Plan(item.Id, CatalogGetItScope.File, null).Plan.Problem.Should().Contain("comes from Whisparr v2");
        }

        [Test]
        public void an_offered_episode_upgrade_should_be_passed_to_sonarr_for_a_profile_checked_search()
        {
            var item = Episode();
            item.Resolution = 1080;
            Mocker.Resolve<IPeerCatalogRepository>().Update(item);
            Mocker.Resolve<ICatalogSeriesRepository>().Insert(new CatalogSeries { ApplicationId = 1, ArrId = 10, TvdbId = item.TvdbId });
            Mocker.Resolve<ICatalogEpisodeRepository>().Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 100, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 50 });
            Mocker.Resolve<ICatalogEpisodeFileRepository>().Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 50, Resolution = 720 });

            Subject.Execute(Queue(item));

            _asked.ExistingArrId.Should().Be(10);
            _asked.UpgradeEpisodes.Should().ContainSingle(e => e.SeasonNumber == 1 && e.EpisodeNumber == 1);
        }

        [TestCase(CatalogGetItScope.Season, null)]
        [TestCase(CatalogGetItScope.Season, 99)]
        [TestCase((CatalogGetItScope)99, null)]
        public void an_invalid_or_unavailable_selection_should_be_refused(CatalogGetItScope scope, int? season)
        {
            var item = Episode();

            Assert.Throws<GetItException>(() => Subject.Plan(item.Id, scope, season));
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<CatalogGetItCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }
    }
}

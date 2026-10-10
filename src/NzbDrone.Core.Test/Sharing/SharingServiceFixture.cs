using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Test.Sharing
{
    [TestFixture]
    public class SharingServiceFixture : DbTest<SharingService, ShareList>
    {
        private static readonly TitleKey Pingu = new(MediaKind.Tv, 1, 10);
        private static readonly TitleKey Bluey = new(MediaKind.Tv, 1, 11);
        private static readonly TitleKey NightOfTheLivingDead = new(MediaKind.Movie, 2, 20);

        private Peer _bob;
        private Peer _carol;
        private Peer _dave;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IShareListRepository>(Mocker.Resolve<ShareListRepository>());
            Mocker.SetConstant<IShareListItemRepository>(Mocker.Resolve<ShareListItemRepository>());
            Mocker.SetConstant<IShareListPeerRepository>(Mocker.Resolve<ShareListPeerRepository>());
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<ICatalogSeriesRepository>(Mocker.Resolve<CatalogSeriesRepository>());
            Mocker.SetConstant<ICatalogEpisodeRepository>(Mocker.Resolve<CatalogEpisodeRepository>());
            Mocker.SetConstant<ICatalogEpisodeFileRepository>(Mocker.Resolve<CatalogEpisodeFileRepository>());
            Mocker.SetConstant<ICatalogMovieRepository>(Mocker.Resolve<CatalogMovieRepository>());
            Mocker.SetConstant<ICatalogMovieFileRepository>(Mocker.Resolve<CatalogMovieFileRepository>());

            Mocker.GetMock<IApplicationFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<ApplicationDefinition> { new() { Id = 1, Name = "Sonarr" }, new() { Id = 2, Name = "Radarr" } });

            var series = Mocker.Resolve<ICatalogSeriesRepository>();
            var episodes = Mocker.Resolve<ICatalogEpisodeRepository>();
            var episodeFiles = Mocker.Resolve<ICatalogEpisodeFileRepository>();
            var movies = Mocker.Resolve<ICatalogMovieRepository>();
            var movieFiles = Mocker.Resolve<ICatalogMovieFileRepository>();

            series.Insert(new CatalogSeries { ApplicationId = 1, ArrId = 10, TvdbId = 75669, Title = "Pingu", Year = 1989, Path = "/tv/Pingu" });
            series.Insert(new CatalogSeries { ApplicationId = 1, ArrId = 11, TvdbId = 353546, Title = "Bluey", Year = 2018, Path = "/tv/Bluey" });

            // Pingu: one single-episode file and one file that holds episodes 4 and 5.
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 101, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, Title = "Hello, Pingu", ArrEpisodeFileId = 501 });
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 104, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 4, Title = "The New Arrival", ArrEpisodeFileId = 502 });
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 105, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 5, Title = "Pingu Goes Fishing", ArrEpisodeFileId = 502 });
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 201, ArrSeriesId = 11, SeasonNumber = 1, EpisodeNumber = 1, Title = "Magic Xylophone", ArrEpisodeFileId = 601 });

            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 501, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/Season 1/Pingu - S01E01.mkv", RelativePath = "Season 1/Pingu - S01E01.mkv", Size = 100, Quality = "WEBDL-720p", Resolution = 720 });
            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 502, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/Season 1/Pingu - S01E04-E05.mkv", RelativePath = "Season 1/Pingu - S01E04-E05.mkv", SceneName = "Pingu.S01E04E05.720p.WEB-DL-GROUP", Size = 200, Quality = "WEBDL-720p", Resolution = 720 });
            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 601, ArrSeriesId = 11, SeasonNumber = 1, Path = "/tv/Bluey/Season 1/Bluey - S01E01.mkv", RelativePath = "Season 1\\Bluey - S01E01.mkv", Size = 300, Quality = "Bluray-1080p", Resolution = 1080 });

            movies.Insert(new CatalogMovie { ApplicationId = 2, ArrId = 20, TmdbId = 10331, ImdbId = "tt0063350", Title = "Night of the Living Dead", Year = 1968, ArrMovieFileId = 701 });
            movies.Insert(new CatalogMovie { ApplicationId = 2, ArrId = 21, TmdbId = 10513, Title = "Plan 9 from Outer Space", Year = 1957, ArrMovieFileId = 702 });

            movieFiles.Insert(new CatalogMovieFile { ApplicationId = 2, ArrId = 701, ArrMovieId = 20, Path = "/movies/Night of the Living Dead (1968)/movie.mkv", RelativePath = "movie.mkv", Size = 400, Quality = "WEBDL-720p", Resolution = 720 });

            // Radarr stopped reporting this one.
            movieFiles.Insert(new CatalogMovieFile { ApplicationId = 2, ArrId = 702, ArrMovieId = 21, Path = "/movies/Plan 9/movie.mkv", RelativePath = "movie.mkv", Size = 500, MissingSince = DateTime.UtcNow });

            _bob = GivenPeer("Bob", PeerState.Trusted);
            _carol = GivenPeer("Carol", PeerState.Trusted);
            _dave = GivenPeer("Dave", PeerState.Revoked);
        }

        private Peer GivenPeer(string name, PeerState state)
        {
            return Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = name.ToLowerInvariant(), Name = name, State = state, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });
        }

        private ShareListDetails Hidden => Subject.All().Single(l => l.List.IsHidden);

        private List<string> OfferedTo(Peer peer)
        {
            return Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(peer.Id)).Items.Select(i => i.Id).ToList();
        }

        [Test]
        public void a_trusted_peer_should_be_offered_nothing_by_default()
        {
            OfferedTo(_bob).Should().BeEmpty();
        }

        [Test]
        public void share_everything_should_offer_every_file_the_arr_apps_still_report()
        {
            Subject.SetShareAll(_bob.Id, true);

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502", "e-1-601", "m-2-701");
            OfferedTo(_carol).Should().BeEmpty();
        }

        [Test]
        public void the_hidden_list_should_override_share_everything()
        {
            Subject.SetShareAll(_bob.Id, true);
            Subject.Update(Hidden.List.Id, null, new[] { Bluey, NightOfTheLivingDead }, null);

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502");
        }

        [Test]
        public void a_share_list_should_offer_only_its_items_and_only_to_its_peers()
        {
            var list = Subject.Create("Kids");

            Subject.Update(list.List.Id, "Kids", new[] { Pingu }, new[] { _bob.Id });

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502");
            OfferedTo(_carol).Should().BeEmpty();
        }

        [Test]
        public void the_hidden_list_should_override_a_share_list()
        {
            var list = Subject.Create("Everything");

            Subject.Update(list.List.Id, null, new[] { Pingu, Bluey, NightOfTheLivingDead }, new[] { _bob.Id });
            Subject.Update(Hidden.List.Id, null, new[] { Pingu }, null);

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-601", "m-2-701");
        }

        [Test]
        public void a_peer_that_is_not_trusted_should_be_offered_nothing_whatever_is_shared()
        {
            var list = Subject.Create("Everything");

            Subject.Update(list.List.Id, null, new[] { Pingu, Bluey }, new[] { _dave.Id });
            Subject.SetShareAll(_dave.Id, true);

            OfferedTo(_dave).Should().BeEmpty();
        }

        [Test]
        public void the_hidden_list_should_never_be_given_to_a_peer()
        {
            Subject.Update(Hidden.List.Id, "Renamed", new[] { Pingu }, new[] { _bob.Id });

            Hidden.PeerIds.Should().BeEmpty();
            Hidden.List.Name.Should().Be(SharingService.HiddenListName);
            OfferedTo(_bob).Should().BeEmpty();
        }

        [Test]
        public void the_hidden_list_should_not_be_deletable()
        {
            Assert.Throws<InvalidOperationException>(() => Subject.Delete(Hidden.List.Id));
        }

        [Test]
        public void an_offer_should_say_what_a_file_is_and_not_where_it_is()
        {
            Subject.SetShareAll(_bob.Id, true);

            var offer = Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(_bob.Id));
            var json = JsonSerializer.Serialize(offer);

            // Not the folders, and not the file names either.
            json.Should().NotContain("/tv/").And.NotContain("/movies/").And.NotContain("Season 1").And.NotContain(".mkv").And.NotContain("Pingu - S01E01");

            var pingu = offer.Items.Single(i => i.Id == "e-1-501");

            pingu.Should().Match<OfferItem>(i => i.Title == "Pingu" && i.TvdbId == 75669 && i.SeasonNumber == 1 && i.Quality == "WEBDL-720p" && i.Size == 100);
            pingu.SceneName.Should().BeNull();

            // The name the release was grabbed under is sent when the Arr app kept it.
            offer.Items.Single(i => i.Id == "e-1-502").SceneName.Should().Be("Pingu.S01E04E05.720p.WEB-DL-GROUP");
        }

        [Test]
        public void a_multi_episode_file_should_be_offered_once_with_all_its_episodes()
        {
            Subject.SetShareAll(_bob.Id, true);

            var item = Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(_bob.Id)).Items.Single(i => i.Id == "e-1-502");

            item.EpisodeNumbers.Should().Equal(4, 5);
            item.EpisodeTitles.Should().Equal("The New Arrival", "Pingu Goes Fishing");
        }

        [Test]
        public void the_version_should_change_only_when_the_offer_does()
        {
            Subject.SetShareAll(_bob.Id, true);

            var peer = Mocker.Resolve<IPeerRepository>().Get(_bob.Id);
            var first = Subject.BuildOffer(peer).Version;

            Subject.BuildOffer(peer).Version.Should().Be(first);

            Subject.Update(Hidden.List.Id, null, new[] { Bluey }, null);

            Subject.BuildOffer(peer).Version.Should().NotBe(first);
        }

        [Test]
        public void changing_what_is_shared_should_be_announced()
        {
            Subject.SetShareAll(_bob.Id, true);

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<SharingChangedEvent>()), Times.Once());
        }

        [Test]
        public void removing_an_application_should_remove_its_titles_from_every_list()
        {
            var list = Subject.Create("Mixed");

            Subject.Update(list.List.Id, null, new[] { Pingu, NightOfTheLivingDead }, new[] { _bob.Id });
            Subject.Handle(new ProviderDeletedEvent<IApplication>(1));

            Subject.All().Single(l => l.List.Id == list.List.Id).Items.Should().Equal(NightOfTheLivingDead);
        }

        [Test]
        public void deleting_a_peer_should_take_it_off_every_list()
        {
            var list = Subject.Create("Kids");

            Subject.Update(list.List.Id, null, new[] { Pingu }, new[] { _bob.Id, _carol.Id });
            Subject.Handle(new PeerDeletedEvent(_bob.Id));

            Subject.All().Single(l => l.List.Id == list.List.Id).PeerIds.Should().Equal(_carol.Id);
        }

        [Test]
        public void titles_should_list_what_can_be_shared()
        {
            var titles = Subject.GetTitles();

            titles.Select(t => t.Title).Should().Equal("Bluey", "Night of the Living Dead", "Pingu", "Plan 9 from Outer Space");
            titles.Single(t => t.Key == Pingu).Should().Match<ShareableTitle>(t => t.Files == 2 && t.ApplicationName == "Sonarr");
            titles.Single(t => t.Title == "Plan 9 from Outer Space").Files.Should().Be(0);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Nodearr.Api.V5.Catalog;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Catalog
{
    [TestFixture]
    public class CatalogDetailsControllerFixture : TestBase
    {
        private List<CatalogSeries> _series;
        private List<CatalogEpisode> _episodes;
        private List<CatalogEpisodeFile> _episodeFiles;
        private List<CatalogMovie> _movies;
        private List<CatalogMovieFile> _movieFiles;

        [SetUp]
        public void Setup()
        {
            _series = new List<CatalogSeries>();
            _episodes = new List<CatalogEpisode>();
            _episodeFiles = new List<CatalogEpisodeFile>();
            _movies = new List<CatalogMovie>();
            _movieFiles = new List<CatalogMovieFile>();

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(new List<ApplicationDefinition>());
            Mocker.GetMock<ICatalogService>().Setup(s => s.GetSeries(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<int, IEnumerable<int>>((app, ids) => _series.Where(s => s.ApplicationId == app && ids.Contains(s.ArrId)).ToList());
            Mocker.GetMock<ICatalogService>().Setup(s => s.GetEpisodes(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<int, IEnumerable<int>>((app, ids) => _episodes.Where(e => e.ApplicationId == app && ids.Contains(e.ArrId)).ToList());
            Mocker.GetMock<ICatalogService>().Setup(s => s.GetEpisodesForSeries(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<int, IEnumerable<int>>((app, ids) => _episodes.Where(e => e.ApplicationId == app && ids.Contains(e.ArrSeriesId)).ToList());
            Mocker.GetMock<ICatalogService>().Setup(s => s.GetEpisodeFilesForSeries(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<int, IEnumerable<int>>((app, ids) => _episodeFiles.Where(f => f.ApplicationId == app && ids.Contains(f.ArrSeriesId)).ToList());
            Mocker.GetMock<ICatalogService>().Setup(s => s.GetMovies(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<int, IEnumerable<int>>((app, ids) => _movies.Where(m => m.ApplicationId == app && ids.Contains(m.ArrId)).ToList());
            Mocker.GetMock<ICatalogService>().Setup(s => s.GetMovieFiles(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<int, IEnumerable<int>>((app, ids) => _movieFiles.Where(f => f.ApplicationId == app && ids.Contains(f.ArrId)).ToList());
        }

        private LocalCatalogController Local => new(
            Mocker.GetMock<ICatalogService>().Object,
            Mocker.GetMock<IApplicationFactory>().Object,
            Mocker.GetMock<ICatalogFileLocator>().Object);

        [Test]
        public void local_series_and_each_episode_should_show_their_own_arr_details_and_file_resolution()
        {
            _series.Add(new CatalogSeries { ApplicationId = 1, ArrId = 10, Title = "Pingu", Overview = "Series story", Genres = "Family", Network = "SRF", TvdbId = 5 });
            _episodes.Add(new CatalogEpisode { ApplicationId = 1, ArrId = 11, ArrSeriesId = 10, Title = "First", Overview = "First story", SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 20 });
            _episodes.Add(new CatalogEpisode { ApplicationId = 1, ArrId = 12, ArrSeriesId = 10, Title = "Second", Overview = "Second story", SeasonNumber = 1, EpisodeNumber = 2, ArrEpisodeFileId = 20 });
            _episodeFiles.Add(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 20, ArrSeriesId = 10, Resolution = 1080, Quality = "WEBDL-1080p", Size = 100 });
            _episodeFiles.Add(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 21, ArrSeriesId = 10, Resolution = 720 });
            _episodeFiles.Add(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 22, ArrSeriesId = 10, Resolution = 2160, MissingSince = DateTime.UtcNow });

            var series = Local.GetDetails("series", 1, 10).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
            series.Overview.Should().Be("Series story");
            series.Resolutions.Should().Equal(1080, 720);
            series.Episodes.Select(e => e.Title).Should().Equal("First", "Second");
            series.Episodes.Select(e => e.Path).Should().Equal("/catalog/local/details/episode/1/11", "/catalog/local/details/episode/1/12");

            var first = Local.GetDetails("episode", 1, 11).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
            var second = Local.GetDetails("episode", 1, 12).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
            first.Title.Should().Be("First");
            first.Overview.Should().Be("First story");
            second.Title.Should().Be("Second");
            second.Overview.Should().Be("Second story");
            second.Resolution.Should().Be(1080);
        }

        [Test]
        public void an_episode_without_a_file_should_still_have_a_card_until_its_catalog_entry_is_purged()
        {
            _series.Add(new CatalogSeries { ApplicationId = 1, ArrId = 10, Title = "Pingu" });
            _episodes.Add(new CatalogEpisode { ApplicationId = 1, ArrId = 12, ArrSeriesId = 10, Title = "Missing", Overview = "Its story", MissingSince = DateTime.UtcNow });

            var card = Local.GetDetails("episode", 1, 12).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
            card.Overview.Should().Be("Its story");
            card.Resolution.Should().Be(0);
            Local.GetDetails("episode", 2, 12).Result.Should().BeOfType<NotFound>();
            Local.GetDetails("artist", 1, 10).Result.Should().BeOfType<NotFound>();
        }

        [TestCase(null, "movie")]
        [TestCase("movie", "film")]
        [TestCase("scene", "scene")]
        public void local_movie_kinds_should_open_matching_cards(string itemType, string kind)
        {
            _movies.Add(new CatalogMovie { ApplicationId = 2, ArrId = 30, Title = "A title", Overview = "Arr description", ItemType = itemType, Studio = "Studio", ReleaseDate = "2020-01-02", ArrMovieFileId = 40 });
            _movieFiles.Add(new CatalogMovieFile { ApplicationId = 2, ArrId = 40, ArrMovieId = 30, Resolution = 2160, Quality = "Bluray-2160p", Size = 500 });

            var card = Local.GetDetails(kind, 2, 30).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
            card.Kind.Should().Be(kind);
            card.Overview.Should().Be("Arr description");
            card.Resolution.Should().Be(2160);
            Local.GetDetails(kind == "movie" ? "scene" : "movie", 2, 30).Result.Should().BeOfType<NotFound>();
        }

        [Test]
        public void peer_cards_should_use_the_received_offer_and_select_the_correct_episode()
        {
            var offered = new OfferItem
            {
                Id = "e-1-20", Kind = MediaKind.Tv, Title = "Pingu", Overview = "Series story",
                EpisodeNumbers = new List<int> { 1, 2 }, EpisodeTitles = new List<string> { "First", "Second" },
                EpisodeOverviews = new List<string> { "First story", "Second story" }, SeasonNumber = 1,
                Resolution = 720, Quality = "WEBDL-720p", Size = 100
            };
            var item = PeerCatalogItem.FromOffer(3, offered);
            item.Id = 42;
            var otherPeer = PeerCatalogItem.FromOffer(4, new OfferItem
            {
                Id = "e-2-30", Kind = MediaKind.Tv, Title = "Pingu", SeasonNumber = 2,
                EpisodeNumbers = new List<int> { 1 }, EpisodeTitles = new List<string> { "Other friend" }, Resolution = 1080
            });
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetItem(42)).Returns(item);
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetSummaries()).Returns(new List<PeerCatalogSummary> { new() { PeerId = 3, PeerName = "Alice" } });
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetSeries(null)).Returns(new List<PeerCatalogSeries>
            {
                new()
                {
                    Key = NzbDrone.Core.Posters.PeerPosters.KeyOf(item),
                    Items = new List<PeerCatalogItem> { item, otherPeer }
                }
            });
            var controller = new PeerCatalogController(Mocker.GetMock<IPeerCatalogService>().Object);

            var series = controller.GetDetails("series", 42).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
            var episode = controller.GetDetails("episode", 42, 1).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
            series.Overview.Should().Be("Series story");
            series.Resolutions.Should().Equal(1080, 720);
            series.Quality.Should().BeNull();
            series.Episodes.Select(e => e.Title).Should().Equal("First", "Second");
            series.Episodes.Select(e => e.Path).Should().Equal("/catalog/peers/details/episode/42?episodeIndex=0", "/catalog/peers/details/episode/42?episodeIndex=1");
            episode.Title.Should().Be("Second");
            episode.Overview.Should().Be("Second story");
            episode.SourceName.Should().Be("Alice");
            episode.Resolution.Should().Be(720);
            controller.GetDetails("episode", 42, 2).Result.Should().BeOfType<NotFound>();
            controller.GetDetails("episode", 42, -1).Result.Should().BeOfType<NotFound>();
            controller.GetDetails("episode", 42).Result.Should().BeOfType<NotFound>();
            controller.GetDetails("series", 999).Result.Should().BeOfType<NotFound>();
        }

        [TestCase(false, false, "movie")]
        [TestCase(true, false, "film")]
        [TestCase(true, true, "scene")]
        public void peer_movie_kinds_should_open_only_the_matching_card(bool adult, bool scene, string kind)
        {
            var item = PeerCatalogItem.FromOffer(3, new OfferItem
            {
                Id = "m-1-40", Kind = MediaKind.Movie, Title = "A title", Adult = adult,
                ItemType = scene ? "scene" : "movie", Studio = "Studio", Overview = "Peer description",
                Resolution = 1080
            });
            item.Id = 42;
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetItem(42)).Returns(item);
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetSummaries()).Returns(new List<PeerCatalogSummary>());
            var controller = new PeerCatalogController(Mocker.GetMock<IPeerCatalogService>().Object);

            var card = controller.GetDetails(kind, 42).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
            card.Overview.Should().Be("Peer description");
            card.Resolution.Should().Be(1080);
            controller.GetDetails("episode", 42, 0).Result.Should().BeOfType<NotFound>();
        }
    }
}

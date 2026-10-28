using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Nodearr.Api.V5.Catalog;
using Nodearr.Api.V5.Sharing;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Sharing
{
    [TestFixture]
    public class ReceivedDetailsFixture : TestBase<SendController>
    {
        private Peer _peer;
        private ReceivedTitle _received;
        private List<ReceivedTitle> _waiting;
        private List<PeerCatalogItem> _items;

        [SetUp]
        public void Setup()
        {
            _peer = new Peer { Id = 3, Name = "Alice", State = PeerState.Trusted };
            _received = new ReceivedTitle { Id = 7, PeerId = 3, Title = "Pingu", MediaKind = MediaKind.Tv, IsWhole = true, OfferIds = "e-1,e-2" };
            _waiting = new List<ReceivedTitle> { _received };
            var first = PeerCatalogItem.FromOffer(3, new OfferItem
            {
                Id = "e-1", Kind = MediaKind.Tv, Title = "Pingu", TvdbId = 5, Overview = "The series", Genres = "Family",
                SeasonNumber = 1, EpisodeNumbers = new List<int> { 1, 2 }, EpisodeTitles = new List<string> { "First", "Second" },
                EpisodeOverviews = new List<string> { "First story", "Second story" }, Resolution = 720
            });
            first.Id = 42;
            var second = PeerCatalogItem.FromOffer(3, new OfferItem
            {
                Id = "e-2", Kind = MediaKind.Tv, Title = "Pingu", TvdbId = 5, SeasonNumber = 2, EpisodeNumbers = new List<int> { 3 }
            });
            second.Id = 43;
            _items = new List<PeerCatalogItem> { first, second };
            Mocker.GetMock<IReceivedService>().Setup(s => s.Waiting()).Returns(_waiting);
            Mocker.GetMock<IPeerService>().Setup(s => s.All()).Returns(new List<Peer> { _peer });
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.FindItemId(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
                .Returns<int, IEnumerable<string>>((peer, ids) => _items.FirstOrDefault(i => i.PeerId == peer && ids.Contains(i.RemoteId))?.Id);
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetItem(It.IsAny<int>()))
                .Returns<int>(id => _items.FirstOrDefault(i => i.Id == id));
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetSummaries())
                .Returns(new List<PeerCatalogSummary> { new() { PeerId = 3, PeerName = "Alice" } });
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetSeries(null))
                .Returns(() => new List<PeerCatalogSeries> { new() { Key = NzbDrone.Core.Posters.PeerPosters.KeyOf(first), Items = _items } });
            Mocker.GetMock<ILocalHoldingsFactory>().Setup(f => f.Load()).Returns(new LocalHoldings(
                Array.Empty<CatalogSeries>(),
                Array.Empty<CatalogEpisode>(),
                Array.Empty<CatalogEpisodeFile>(),
                Array.Empty<CatalogMovie>(),
                Array.Empty<CatalogMovieFile>()));
        }

        private CatalogDetailsResource Details => Subject.GetReceivedDetails(7).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;

        [Test]
        public void whole_series_should_use_the_existing_cached_card_without_contacting_the_friend()
        {
            Subject.GetReceived().Value.Single().DetailsAvailable.Should().BeTrue();
            var details = Details;
            details.Kind.Should().Be("series");
            details.Overview.Should().Be("The series");
            details.Genres.Should().Be("Family");
            details.SourceName.Should().Be("Alice");
            details.Episodes.Should().HaveCount(3).And.OnlyContain(e => !e.WasSent);
            details.GetIt.Scope.Should().Be(CatalogGetItScope.Title);
            Mocker.GetMock<IPeerCatalogService>().Verify(s => s.Sync(It.IsAny<int?>()), Times.Never());
            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Never());
        }

        [Test]
        public void one_episode_should_select_its_index_inside_a_multi_episode_file()
        {
            _received.IsWhole = false;
            _received.Summary = "S01E02";
            _received.Parts = JsonSerializer.Serialize(new SentParts { Episodes = new List<SeasonEpisode> { new() { SeasonNumber = 1, EpisodeNumber = 2 } } });
            var details = Details;
            details.Kind.Should().Be("episode");
            details.Title.Should().Be("Second");
            details.Overview.Should().Be("Second story");
            details.EpisodeNumber.Should().Be(2);
            details.GetIt.Scope.Should().Be(CatalogGetItScope.File);
            details.SentSummary.Should().Be("S01E02");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void partial_sends_should_mark_only_the_sent_seasons_or_episodes(bool season)
        {
            _received.IsWhole = false;
            _received.Parts = JsonSerializer.Serialize(season
                ? new SentParts { Seasons = new List<int> { 1 } }
                : new SentParts { Episodes = new List<SeasonEpisode> { new() { SeasonNumber = 1, EpisodeNumber = 1 }, new() { SeasonNumber = 2, EpisodeNumber = 3 } } });
            var details = Details;
            details.Kind.Should().Be("series");
            details.Episodes.Select(e => e.WasSent).Should().Equal(season ? new[] { true, true, false } : new[] { true, false, true });
        }

        [TestCase(false, false, "movie")]
        [TestCase(true, false, "film")]
        [TestCase(true, true, "scene")]
        public void movie_sends_should_open_the_matching_kind(bool adult, bool scene, string kind)
        {
            _received.MediaKind = MediaKind.Movie;
            _items[0].MediaKind = MediaKind.Movie;
            _items[0].IsAdult = adult;
            _items[0].ItemType = scene ? "scene" : "movie";
            Details.Kind.Should().Be(kind);
        }

        [Test]
        public void withdrawn_offer_should_not_fall_back_to_another_friends_copy()
        {
            _items.ForEach(i => i.PeerId = 9);
            Subject.GetReceived().Value.Single().DetailsAvailable.Should().BeFalse();
            Subject.GetReceivedDetails(7).Result.Should().BeOfType<NotFound>();
        }

        [Test]
        public void revoked_trust_should_make_even_a_cached_offer_unavailable()
        {
            _peer.State = PeerState.Revoked;
            Subject.GetReceived().Value.Single().DetailsAvailable.Should().BeFalse();
            Subject.GetReceivedDetails(7).Result.Should().BeOfType<NotFound>();
        }

        [Test]
        public void dismissed_or_unknown_send_should_not_open_details()
        {
            _waiting.Clear();
            Subject.GetReceivedDetails(7).Result.Should().BeOfType<NotFound>();
            Subject.GetReceivedDetails(999).Result.Should().BeOfType<NotFound>();
        }

        [Test]
        public void music_should_keep_its_poster_without_offering_an_unsupported_card()
        {
            _received.MediaKind = MediaKind.Music;
            _items[0].MediaKind = MediaKind.Music;
            var note = Subject.GetReceived().Value.Single();
            note.PosterItemId.Should().Be(42);
            note.DetailsAvailable.Should().BeFalse();
            Subject.GetReceivedDetails(7).Result.Should().BeOfType<NotFound>();
        }
    }
}

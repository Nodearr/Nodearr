using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PeerCatalog
{
    [TestFixture]
    public class PeerCatalogServiceFixture : DbTest<PeerCatalogService, PeerCatalogItem>
    {
        private Peer _alice;
        private List<OfferItem> _offered;
        private string _version;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted });

            // Locally: Pingu is in Sonarr, with no files at all.
            Mocker.GetMock<ILocalHoldingsFactory>()
                  .Setup(f => f.Load())
                  .Returns(() => new LocalHoldings(
                      new[] { new CatalogSeries { ApplicationId = 1, ArrId = 10, TvdbId = 75669 } },
                      Enumerable.Empty<CatalogEpisode>(),
                      Enumerable.Empty<CatalogEpisodeFile>(),
                      Enumerable.Empty<CatalogMovie>(),
                      Enumerable.Empty<CatalogMovieFile>()));

            GivenOffer("v1", 3);

            // Alice answers the way OfferExchange does, two items to a page.
            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.Is<PeerMessage>(m => m.Type == PeerMessageTypes.Catalog)))
                  .Returns<Peer, PeerMessage>((_, request) =>
                  {
                      if (request.CatalogPage == 0 && request.CatalogKnownVersion == _version)
                      {
                          return new PeerReply { Ok = true, CatalogVersion = _version, CatalogUnchanged = true };
                      }

                      return new PeerReply
                      {
                          Ok = true,
                          CatalogVersion = _version,
                          CatalogTotalPages = (_offered.Count + 1) / 2,
                          CatalogItems = _offered.Skip(request.CatalogPage * 2).Take(2).ToList()
                      };
                  });
        }

        private void GivenOffer(string version, int episodes)
        {
            _version = version;
            _offered = Enumerable.Range(1, episodes)
                .Select(n => new OfferItem { Id = "e-1-" + n, Kind = MediaKind.Tv, TvdbId = 75669, Title = "Pingu", SeasonNumber = 1, EpisodeNumbers = new List<int> { n }, Resolution = 720 })
                .ToList();
        }

        private List<PeerCatalogItem> Stored => Mocker.Resolve<IPeerCatalogRepository>().ForPeer(_alice.Id);
        private Peer StoredPeer => Mocker.Resolve<IPeerRepository>().Get(_alice.Id);

        [Test]
        public void should_store_every_page_of_what_a_peer_offers()
        {
            Subject.Sync(_alice.Id);

            Stored.Select(i => i.RemoteId).Should().BeEquivalentTo("e-1-1", "e-1-2", "e-1-3");
            StoredPeer.Should().Match<Peer>(p => p.CatalogVersion == "v1" && p.CatalogSyncedAt != null && p.CatalogError == null);
        }

        [Test]
        public void should_compare_what_is_offered_with_the_local_catalog()
        {
            Subject.Sync(_alice.Id);

            Stored.Should().OnlyContain(i => i.LocalStatus == PeerItemStatus.Missing);
            Subject.GetSummaries().Single().Should().Match<PeerCatalogSummary>(s => s.Missing == 3 && s.Have == 0 && s.PeerName == "Alice");
        }

        [Test]
        public void should_drop_what_a_peer_stops_offering()
        {
            Subject.Sync(_alice.Id);

            GivenOffer("v2", 1);
            Subject.Sync(_alice.Id);

            Stored.Select(i => i.RemoteId).Should().Equal("e-1-1");
            StoredPeer.CatalogVersion.Should().Be("v2");
        }

        [Test]
        public void should_keep_what_it_has_when_nothing_changed()
        {
            Subject.Sync(_alice.Id);
            var ids = Stored.Select(i => i.Id).ToList();

            Subject.Sync(_alice.Id);

            Stored.Select(i => i.Id).Should().Equal(ids);
        }

        [Test]
        public void should_keep_what_it_has_when_the_peer_is_unreachable()
        {
            Subject.Sync(_alice.Id);

            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()))
                  .Throws(new TransportException(TransportFailure.NoRoute, "no route"));

            Subject.Sync(_alice.Id);

            Stored.Should().HaveCount(3);
            StoredPeer.Should().Match<Peer>(p => p.CatalogError == "no route" && p.CatalogVersion == "v1");
        }

        [Test]
        public void should_forget_a_peers_catalog_when_the_peer_refuses()
        {
            Subject.Sync(_alice.Id);

            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()))
                  .Throws(new TransportException(TransportFailure.NotAuthorized, "refused"));

            Subject.Sync(_alice.Id);

            Stored.Should().BeEmpty();
            StoredPeer.CatalogVersion.Should().BeNull();
        }

        [TestCase(PeerState.Blocked)]
        [TestCase(PeerState.Revoked)]
        public void should_forget_a_peers_catalog_when_trust_ends(PeerState state)
        {
            Subject.Sync(_alice.Id);

            _alice.State = state;
            Subject.HandleAsync(new PeerStateChangedEvent(_alice, PeerState.Trusted));

            Stored.Should().BeEmpty();
            StoredPeer.CatalogVersion.Should().BeNull();
        }

        [Test]
        public void should_fetch_a_peers_catalog_when_it_becomes_trusted()
        {
            Subject.HandleAsync(new PeerStateChangedEvent(_alice, PeerState.Pending));

            Stored.Should().HaveCount(3);
        }

        [Test]
        public void should_fetch_again_when_the_peer_says_its_offer_changed()
        {
            Subject.Sync(_alice.Id);

            GivenOffer("v2", 2);
            Subject.HandleAsync(new PeerCatalogChangedEvent(_alice.Id));

            Stored.Should().HaveCount(2);
        }

        [Test]
        public void should_forget_the_catalog_of_a_deleted_peer()
        {
            Subject.Sync(_alice.Id);

            Subject.Handle(new PeerDeletedEvent(_alice.Id));

            Stored.Should().BeEmpty();
        }

        [Test]
        public void should_compare_again_after_the_local_catalog_is_refreshed()
        {
            Subject.Sync(_alice.Id);

            // Sonarr no longer has Pingu.
            Mocker.GetMock<ILocalHoldingsFactory>()
                  .Setup(f => f.Load())
                  .Returns(new LocalHoldings(
                      Enumerable.Empty<CatalogSeries>(),
                      Enumerable.Empty<CatalogEpisode>(),
                      Enumerable.Empty<CatalogEpisodeFile>(),
                      Enumerable.Empty<CatalogMovie>(),
                      Enumerable.Empty<CatalogMovieFile>()));

            Subject.HandleAsync(new CatalogRefreshedEvent(1));

            Stored.Should().OnlyContain(i => i.LocalStatus == PeerItemStatus.NotInLibrary);
        }

        [Test]
        public void should_not_ask_a_peer_that_is_not_trusted()
        {
            _alice.State = PeerState.Revoked;
            Mocker.Resolve<IPeerRepository>().Update(_alice);

            Subject.Sync(null);

            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Never());
        }
    }
}

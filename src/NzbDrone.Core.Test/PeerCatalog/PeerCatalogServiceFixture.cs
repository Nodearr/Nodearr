using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

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

            Mocker.GetMock<ICandidateIdProvider>()
                  .Setup(p => p.For(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns<string, string>((nodeId, remoteId) => $"{nodeId}/{remoteId}");

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
        public async Task overlapping_syncs_should_use_the_latest_version_and_store_each_offer_once()
        {
            var versions = new ConcurrentQueue<string>();
            using var started = new Barrier(2);
            using var overlapping = new ManualResetEventSlim();
            var fullReads = 0;
            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.Is<PeerMessage>(m => m.Type == PeerMessageTypes.Catalog)))
                  .Returns<Peer, PeerMessage>((_, request) =>
                  {
                      if (request.CatalogPage == 0)
                      {
                          versions.Enqueue(request.CatalogKnownVersion);
                          if (request.CatalogKnownVersion == _version)
                          {
                              return new PeerReply { Ok = true, CatalogVersion = _version, CatalogUnchanged = true };
                          }

                          // A slow first reply lets the competing request begin. Before the fix
                          // both fetched the same whole catalog from an empty local snapshot.
                          if (Interlocked.Increment(ref fullReads) == 1)
                          {
                              overlapping.Wait(TimeSpan.FromSeconds(1));
                          }
                          else
                          {
                              overlapping.Set();
                          }
                      }

                      return new PeerReply
                      {
                          Ok = true,
                          CatalogVersion = _version,
                          CatalogTotalPages = 2,
                          CatalogItems = _offered.Skip(request.CatalogPage * 2).Take(2).ToList()
                      };
                  });
            var service = Subject;
            await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            {
                started.SignalAndWait();
                service.Sync((int?)_alice.Id);
            })));

            versions.Should().Equal(new string[] { null, "v1" });
            Stored.Select(i => i.RemoteId).Should().BeEquivalentTo("e-1-1", "e-1-2", "e-1-3");
        }

        [Test]
        public void should_give_each_file_the_id_and_the_title_the_arr_apps_will_see()
        {
            Subject.Sync(_alice.Id);

            Stored.Select(i => i.CandidateId).Should().BeEquivalentTo("alice/e-1-1", "alice/e-1-2", "alice/e-1-3");
            Stored.Select(i => i.ReleaseTitle).Should().BeEquivalentTo("Pingu.S01E01", "Pingu.S01E02", "Pingu.S01E03");
        }

        [Test]
        public void should_keep_the_days_a_peer_gives_and_drop_anything_that_is_not_a_day()
        {
            _offered[0].AirDates = new List<string> { "2024-05-17", "2024-5-18", "2024-13-45", "../../etc", string.Empty, null, "2024-05-19" };

            Subject.Sync(_alice.Id);

            Stored.Single(i => i.RemoteId == _offered[0].Id).GetAirDates().Should().Equal("2024-05-17", "2024-05-19");
            Stored.Where(i => i.RemoteId != _offered[0].Id).Should().OnlyContain(i => i.GetAirDates().Count == 0);
        }

        [TestCase("anime", "anime")]
        [TestCase("daily", "daily")]
        [TestCase("standard", "standard")]
        [TestCase(null, null)]
        [TestCase("not-a-series-type", null)]
        public void should_keep_only_known_series_types_and_accept_older_offers(string offered, string kept)
        {
            _offered[0].SeriesType = offered;

            Subject.Sync(_alice.Id);

            Stored.Single(i => i.RemoteId == _offered[0].Id).SeriesType.Should().Be(kept);
        }

        [Test]
        public void should_keep_scene_numbers_that_fit_the_file_and_title_it_by_them()
        {
            _offered[0].SceneSeasonNumber = 2;
            _offered[0].SceneEpisodeNumbers = new List<int> { 7 };

            Subject.Sync(_alice.Id);

            var item = Stored.Single(i => i.RemoteId == _offered[0].Id);

            item.Should().Match<PeerCatalogItem>(i => i.SceneSeasonNumber == 2 && i.SceneEpisodeNumbers == "7" && i.SeasonNumber == 1);
            item.ReleaseTitle.Should().Be("Pingu.S02E07");
            Stored.Where(i => i.RemoteId != _offered[0].Id).Should().OnlyContain(i => !i.HasSceneNumbers);
        }

        [TestCase(2, new[] { 7, 8 })]
        [TestCase(-1, new[] { 7 })]
        [TestCase(2, new[] { -3 })]
        [TestCase(null, new[] { 7 })]
        [TestCase(2, new int[0])]
        public void should_drop_scene_numbers_that_do_not_fit_the_file(int? season, int[] episodes)
        {
            _offered[0].SceneSeasonNumber = season;
            _offered[0].SceneEpisodeNumbers = episodes.ToList();

            Subject.Sync(_alice.Id);

            Stored.Single(i => i.RemoteId == _offered[0].Id).Should().Match<PeerCatalogItem>(i => !i.HasSceneNumbers && i.ReleaseTitle == "Pingu.S01E01");
        }

        [Test]
        public void should_keep_one_of_two_files_a_peer_gives_the_same_name()
        {
            _offered.Add(new OfferItem { Id = "e-1-1", Kind = MediaKind.Tv, TvdbId = 75669, Title = "Pingu", SeasonNumber = 1, EpisodeNumbers = new List<int> { 9 } });

            Subject.Sync(_alice.Id);

            Stored.Select(i => i.RemoteId).Should().BeEquivalentTo("e-1-1", "e-1-2", "e-1-3");
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

        // A node that is not genuine can claim a catalog of any size. What this node already holds
        // is kept, nothing of the claimed one is, and it stops after the first page.
        [TestCase(401, 0)]
        [TestCase(1, 200001)]
        public void should_refuse_a_catalog_larger_than_it_takes(int pages, int files)
        {
            Subject.Sync(_alice.Id);
            Mocker.GetMock<IPeerService>().Invocations.Clear();

            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()))
                  .Returns(new PeerReply { Ok = true, CatalogVersion = "huge", CatalogTotalPages = pages, CatalogItemCount = files, CatalogItems = _offered.Take(2).ToList() });

            Subject.Sync(_alice.Id);

            Stored.Should().HaveCount(3);
            StoredPeer.Should().Match<Peer>(p => p.CatalogVersion == "v1" && p.CatalogError.StartsWith("Its catalog is larger than this node takes"));
            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_refuse_a_page_with_more_than_a_page_holds()
        {
            Subject.Sync(_alice.Id);

            var overfull = Enumerable.Range(0, OfferExchange.PageSize + 1)
                .Select(n => new OfferItem { Id = "e-2-" + n, Kind = MediaKind.Tv, TvdbId = 75669, Title = "Pingu", SeasonNumber = 2, EpisodeNumbers = new List<int> { n } })
                .ToList();

            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()))
                  .Returns<Peer, PeerMessage>((_, request) => new PeerReply { Ok = true, CatalogVersion = "big", CatalogTotalPages = 2, CatalogItems = request.CatalogPage == 0 ? _offered.Take(2).ToList() : overfull });

            Subject.Sync(_alice.Id);

            Stored.Should().HaveCount(3);
            StoredPeer.Should().Match<Peer>(p => p.CatalogVersion == "v1" && p.CatalogError.StartsWith("Its catalog is larger than this node takes"));

            ExceptionVerification.ExpectedWarns(1);
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

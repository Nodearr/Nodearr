using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.PeerCatalog
{
    // Both ends of a catalog exchange: this node's PeerCatalogService asks a real OfferExchange,
    // through messages turned into bytes and back as they are on the wire.
    [TestFixture]
    public class CatalogChangesFixture : DbTest<PeerCatalogService, PeerCatalogItem>
    {
        private readonly List<PeerReply> _replies = new();
        private Peer _alice;
        private List<OfferItem> _offered;
        private OfferExchange _exchange;

        [SetUp]
        public void Setup()
        {
            _replies.Clear();

            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());
            Mocker.SetConstant<IOfferRecordRepository>(Mocker.Resolve<OfferRecordRepository>());

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted });

            Mocker.GetMock<ILocalHoldingsFactory>()
                  .Setup(f => f.Load())
                  .Returns(() => new LocalHoldings(Enumerable.Empty<CatalogSeries>(), Enumerable.Empty<CatalogEpisode>(), Enumerable.Empty<CatalogEpisodeFile>(), Enumerable.Empty<CatalogMovie>(), Enumerable.Empty<CatalogMovieFile>()));
            Mocker.GetMock<ICandidateIdProvider>().Setup(p => p.For(It.IsAny<string>(), It.IsAny<string>())).Returns<string, string>((node, id) => $"{node}/{id}");

            // Alice's side. What she offers is whatever _offered holds when she is asked.
            var sharing = new Mock<ISharingService>();
            sharing.Setup(s => s.BuildOffer(It.IsAny<Peer>())).Returns(() => Offer.Seal(_offered.Select(Copy).ToList()));
            _exchange = new OfferExchange(sharing.Object, Mocker.Resolve<IOfferRecordRepository>());

            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.Is<PeerMessage>(m => m.Type == PeerMessageTypes.Catalog)))
                  .Returns<Peer, PeerMessage>((peer, request) =>
                  {
                      var reply = PeerReply.FromBytes(_exchange.Answer(peer, PeerMessage.FromBytes(request.ToBytes())).ToBytes());
                      _replies.Add(reply);

                      return reply;
                  });

            _offered = Enumerable.Range(1, 5).Select(Episode).ToList();
        }

        private static OfferItem Episode(int n)
        {
            return new OfferItem { Id = $"e-1-{n}", Kind = MediaKind.Tv, TvdbId = 75669, Title = "Pingu", SeasonNumber = 1, EpisodeNumbers = new List<int> { n }, Resolution = 720, Size = 1000 + n };
        }

        private static OfferItem Copy(OfferItem item)
        {
            return PeerReply.FromBytes(new PeerReply { CatalogItems = new List<OfferItem> { item } }.ToBytes()).CatalogItems[0];
        }

        private List<PeerCatalogItem> Stored => Mocker.Resolve<IPeerCatalogRepository>().ForPeer(_alice.Id);
        private Peer StoredPeer => Mocker.Resolve<IPeerRepository>().Get(_alice.Id);
        private int ItemsSent => _replies.Sum(r => r.CatalogItems?.Count ?? 0);

        private void Sync()
        {
            _replies.Clear();
            Subject.Sync(_alice.Id);
        }

        private void StoredShouldMatchWhatIsOffered()
        {
            var offer = Offer.Seal(_offered.Select(Copy).ToList());

            Stored.Select(i => (i.RemoteId, i.Fingerprint)).Should().BeEquivalentTo(offer.Items.Select(i => (i.Id, i.Fingerprint)));
            StoredPeer.CatalogVersion.Should().Be(offer.Version);
        }

        [Test]
        public void the_first_sync_should_bring_the_whole_catalog()
        {
            Sync();

            _replies.Should().OnlyContain(r => !r.CatalogChanges);
            ItemsSent.Should().Be(5);
            StoredShouldMatchWhatIsOffered();
        }

        [Test]
        public void a_later_sync_should_bring_only_what_was_added_changed_and_removed()
        {
            Sync();

            _offered[1].Resolution = 1080;
            _offered.RemoveAt(4);
            _offered.Add(Episode(9));
            Sync();

            _replies.Should().ContainSingle().Which.Should().Match<PeerReply>(r => r.CatalogChanges && r.CatalogRemoved.Single() == "e-1-5" && r.CatalogItemCount == 5);
            _replies[0].CatalogItems.Select(i => i.Id).Should().BeEquivalentTo("e-1-2", "e-1-9");
            StoredShouldMatchWhatIsOffered();
            Stored.Single(i => i.RemoteId == "e-1-2").Resolution.Should().Be(1080);
        }

        [Test]
        public void nothing_should_be_sent_when_nothing_changed()
        {
            Sync();
            Sync();

            _replies.Should().ContainSingle().Which.CatalogUnchanged.Should().BeTrue();
            ItemsSent.Should().Be(0);
        }

        [Test]
        public void a_file_whose_hash_is_worked_out_later_should_arrive_as_a_change_with_its_hash()
        {
            Sync();

            _offered[0].Blake3 = new string('a', 64);
            Sync();

            _replies.Single().CatalogItems.Single().Id.Should().Be("e-1-1");
            Stored.Single(i => i.RemoteId == "e-1-1").Blake3.Should().Be(new string('a', 64));
            StoredShouldMatchWhatIsOffered();
        }

        [Test]
        public void changes_that_do_not_add_up_here_should_be_followed_by_the_whole_catalog()
        {
            Sync();

            // Something lost one of the files held here.
            Mocker.Resolve<IPeerCatalogRepository>().DeleteForPeer(_alice.Id, new[] { "e-1-3" });

            _offered[0].Resolution = 1080;
            Sync();

            _replies.First().CatalogChanges.Should().BeTrue();
            _replies.Last().CatalogChanges.Should().BeFalse();
            StoredShouldMatchWhatIsOffered();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_sender_that_lost_its_record_should_send_the_whole_catalog()
        {
            Sync();
            Mocker.Resolve<IOfferRecordRepository>().DeleteForPeer(_alice.Id);

            _offered[0].Resolution = 1080;
            Sync();

            _replies.Should().OnlyContain(r => !r.CatalogChanges);
            ItemsSent.Should().Be(5);
            StoredShouldMatchWhatIsOffered();
        }

        [Test]
        public void a_node_that_cannot_take_changes_should_be_sent_the_whole_catalog()
        {
            Sync();
            _offered[0].Resolution = 1080;

            var reply = _exchange.Answer(_alice, new PeerMessage { Type = PeerMessageTypes.Catalog, CatalogKnownVersion = StoredPeer.CatalogVersion });

            reply.CatalogChanges.Should().BeFalse();
            reply.CatalogItems.Should().HaveCount(5);
        }

        [Test]
        public void when_most_of_a_large_catalog_changed_the_whole_of_it_should_be_sent()
        {
            _offered = Enumerable.Range(1, OfferExchange.PageSize * 3).Select(Episode).ToList();
            Sync();

            foreach (var item in _offered.Take(OfferExchange.PageSize * 2))
            {
                item.Resolution = 1080;
            }

            Sync();

            _replies.Should().OnlyContain(r => !r.CatalogChanges);
            StoredShouldMatchWhatIsOffered();
        }

        [Test]
        public void everything_unshared_should_arrive_as_removals_only()
        {
            Sync();

            _offered.Clear();
            Sync();

            _replies.Single().Should().Match<PeerReply>(r => r.CatalogChanges && r.CatalogRemoved.Count == 5 && r.CatalogItems.Count == 0);
            Stored.Should().BeEmpty();
        }

        [Test]
        public void a_hash_that_is_not_one_should_not_be_kept()
        {
            _offered[0].Blake3 = "../../etc/passwd";
            _offered[1].Blake3 = new string('A', 64);

            Sync();

            Stored.Where(i => i.RemoteId is "e-1-1" or "e-1-2").Should().OnlyContain(i => i.Blake3 == null);
        }
    }
}

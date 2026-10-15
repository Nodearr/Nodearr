using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Sharing
{
    [TestFixture]
    public class SharingNotifierFixture : CoreTest<SharingNotifier>
    {
        private readonly Peer _bob = new() { Id = 2, NodeId = "bob", Name = "Bob", State = PeerState.Trusted };
        private List<string> _offered;

        [SetUp]
        public void Setup()
        {
            _offered = new List<string> { "e-1-1", "e-1-2" };

            Mocker.GetMock<IPeerRepository>().Setup(p => p.All()).Returns(() => new List<Peer> { _bob });
            Mocker.GetMock<ISharingService>().Setup(s => s.BuildOffer(It.IsAny<Peer>())).Returns(() => new Offer { Items = _offered.Select(id => new OfferItem { Id = id }).ToList() });
            GivenSent("e-1-1", "e-1-2");
        }

        private void GivenSent(params string[] ids)
        {
            Mocker.GetMock<IOfferRecordRepository>()
                  .Setup(r => r.FindForPeer(_bob.Id))
                  .Returns(new OfferRecord { PeerId = _bob.Id, Version = "v1", Fingerprints = OfferRecord.Write(ids.Select(id => new OfferItem { Id = id, Fingerprint = "0123456789abcdef" })) });
        }

        private void VerifyTold(Times times)
        {
            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(_bob, It.Is<PeerMessage>(m => m.Type == PeerMessageTypes.CatalogChanged)), times);
        }

        [Test]
        public void a_peer_should_be_told_at_once_when_something_it_holds_is_taken_back()
        {
            _offered.Remove("e-1-2");

            Subject.HandleAsync(new SharingChangedEvent());

            VerifyTold(Times.Once());
        }

        // As a new release waits for Sonarr's next RSS Sync.
        [Test]
        public void something_newly_shared_should_wait_for_the_peers_next_sync()
        {
            _offered.Add("e-1-3");

            Subject.HandleAsync(new SharingChangedEvent());

            VerifyTold(Times.Never());
        }

        [Test]
        public void a_file_gone_from_the_arr_app_should_be_taken_back_at_once_too()
        {
            _offered.Remove("e-1-1");

            Subject.HandleAsync(new CatalogRefreshedEvent(1));

            VerifyTold(Times.Once());
        }

        [Test]
        public void a_peer_that_was_never_sent_anything_should_not_be_told()
        {
            Mocker.GetMock<IOfferRecordRepository>().Setup(r => r.FindForPeer(_bob.Id)).Returns((OfferRecord)null);
            _offered.Clear();

            Subject.HandleAsync(new SharingChangedEvent());

            VerifyTold(Times.Never());
        }

        [Test]
        public void a_peer_that_is_not_trusted_should_not_be_told()
        {
            _bob.State = PeerState.Revoked;
            _offered.Clear();

            Subject.HandleAsync(new SharingChangedEvent());

            VerifyTold(Times.Never());
            _bob.State = PeerState.Trusted;
        }

        [Test]
        public void a_peer_that_cannot_be_reached_should_be_left_to_find_out_at_its_next_sync()
        {
            _offered.Clear();
            Mocker.GetMock<IPeerService>().Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>())).Throws(new TransportException(TransportFailure.Unavailable, "offline"));

            Subject.HandleAsync(new SharingChangedEvent());

            VerifyTold(Times.Once());
        }
    }
}

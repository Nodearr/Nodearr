using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Sharing
{
    [TestFixture]
    public class OfferExchangeFixture : CoreTest<OfferExchange>
    {
        private readonly Peer _peer = new() { Id = 7, State = PeerState.Trusted };

        private void GivenOffer(string version, int items)
        {
            Mocker.GetMock<ISharingService>()
                  .Setup(s => s.BuildOffer(_peer, It.IsAny<bool>()))
                  .Returns(new Offer { Version = version, Items = Enumerable.Range(0, items).Select(i => new OfferItem { Id = "e-1-" + i }).ToList() });
        }

        private PeerReply Ask(int page, string version = null, string known = null)
        {
            return Subject.Answer(_peer, new PeerMessage { Type = PeerMessageTypes.Catalog, CatalogPage = page, CatalogVersion = version, CatalogKnownVersion = known });
        }

        [Test]
        public void should_hand_out_a_large_offer_in_pages()
        {
            GivenOffer("v1", (OfferExchange.PageSize * 2) + 10);

            var first = Ask(0);
            var items = new List<OfferItem>(first.CatalogItems);

            first.CatalogTotalPages.Should().Be(3);

            for (var page = 1; page < first.CatalogTotalPages; page++)
            {
                items.AddRange(Ask(page, first.CatalogVersion).CatalogItems);
            }

            items.Select(i => i.Id).Distinct().Should().HaveCount((OfferExchange.PageSize * 2) + 10);
        }

        [Test]
        public void should_say_unchanged_when_the_peer_already_has_this_version()
        {
            GivenOffer("v1", 3);

            Ask(0, known: "v1").Should().Match<PeerReply>(r => r.Ok && r.CatalogUnchanged && r.CatalogItems == null);
        }

        [Test]
        public void should_answer_an_empty_offer_with_one_empty_page()
        {
            GivenOffer("empty", 0);

            Ask(0).Should().Match<PeerReply>(r => r.Ok && r.CatalogTotalPages == 1 && r.CatalogItems.Count == 0);
        }

        [Test]
        public void later_pages_should_come_from_the_snapshot_the_first_page_started()
        {
            GivenOffer("v1", OfferExchange.PageSize + 5);
            var first = Ask(0);

            // Sharing changes between pages. The page already promised still belongs to v1.
            GivenOffer("v2", 1);

            Ask(1, first.CatalogVersion).CatalogItems.Should().HaveCount(5);
            Mocker.GetMock<ISharingService>().Verify(s => s.BuildOffer(_peer, It.IsAny<bool>()), Times.Once());
        }

        [Test]
        public void should_refuse_a_page_of_a_snapshot_that_was_replaced()
        {
            GivenOffer("v1", OfferExchange.PageSize + 5);
            Ask(0);

            GivenOffer("v2", OfferExchange.PageSize + 5);
            Ask(0);

            Ask(1, "v1").Should().Match<PeerReply>(r => !r.Ok && r.Error == "CatalogChanged");
        }

        [Test]
        public void should_refuse_a_later_page_when_no_first_page_was_asked_for()
        {
            Ask(1, "v1").Ok.Should().BeFalse();
        }
    }
}

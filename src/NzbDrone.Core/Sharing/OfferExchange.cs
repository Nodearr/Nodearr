using System;
using System.Collections.Concurrent;
using System.Linq;
using NzbDrone.Core.Nodes;

namespace NzbDrone.Core.Sharing
{
    public interface IOfferExchange
    {
        PeerReply Answer(Peer peer, PeerMessage request);
    }

    // Answers a trusted peer's request for this node's catalog, one page at a time.
    //
    // The first page builds the peer's offer afresh and remembers it, so the following pages come
    // from the same snapshot even if sharing changes in between. A peer that asks for a later page
    // of a snapshot that has been replaced is told to start again.
    public class OfferExchange : IOfferExchange
    {
        public const int PageSize = 500;

        private readonly ISharingService _sharingService;
        private readonly ConcurrentDictionary<int, Offer> _snapshots = new();

        public OfferExchange(ISharingService sharingService)
        {
            _sharingService = sharingService;
        }

        public PeerReply Answer(Peer peer, PeerMessage request)
        {
            Offer offer;

            if (request.CatalogPage <= 0)
            {
                offer = _sharingService.BuildOffer(peer);
                _snapshots[peer.Id] = offer;

                if (offer.Version == request.CatalogKnownVersion)
                {
                    return new PeerReply { Ok = true, CatalogVersion = offer.Version, CatalogUnchanged = true };
                }
            }
            else if (!_snapshots.TryGetValue(peer.Id, out offer) || offer.Version != request.CatalogVersion)
            {
                return new PeerReply { Ok = false, Error = "CatalogChanged" };
            }

            var page = Math.Max(request.CatalogPage, 0);

            return new PeerReply
            {
                Ok = true,
                CatalogVersion = offer.Version,
                CatalogTotalPages = Math.Max(1, (int)Math.Ceiling(offer.Items.Count / (double)PageSize)),
                CatalogItems = offer.Items.Skip(page * PageSize).Take(PageSize).ToList()
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.PeerCatalog
{
    public interface IPeerCatalogService
    {
        void Sync(int? peerId);
        List<PeerCatalogSummary> GetSummaries();
        PagingSpec<PeerCatalogItem> Paged(PagingSpec<PeerCatalogItem> pagingSpec);
    }

    // Keeps a copy of what each trusted peer offers, and how it compares with the local catalog.
    public class PeerCatalogService : IPeerCatalogService,
                                      IExecute<PeerCatalogSyncCommand>,
                                      IHandleAsync<PeerStateChangedEvent>,
                                      IHandleAsync<PeerCatalogChangedEvent>,
                                      IHandleAsync<CatalogRefreshedEvent>,
                                      IHandle<PeerDeletedEvent>
    {
        private const int MaxAttempts = 3;
        private const int MaxPages = 10000;

        private readonly IPeerRepository _peers;
        private readonly IPeerService _peerService;
        private readonly IPeerCatalogRepository _items;
        private readonly ILocalHoldingsFactory _holdings;
        private readonly ICandidateIdProvider _candidateIds;
        private readonly Logger _logger;

        public PeerCatalogService(IPeerRepository peers, IPeerService peerService, IPeerCatalogRepository items, ILocalHoldingsFactory holdings, ICandidateIdProvider candidateIds, Logger logger)
        {
            _peers = peers;
            _peerService = peerService;
            _items = items;
            _holdings = holdings;
            _candidateIds = candidateIds;
            _logger = logger;
        }

        public void Execute(PeerCatalogSyncCommand message)
        {
            Sync(message.PeerId);
        }

        public void Sync(int? peerId)
        {
            foreach (var peer in _peers.All().Where(p => p.State == PeerState.Trusted && (!peerId.HasValue || p.Id == peerId.Value)))
            {
                Sync(peer);
            }
        }

        public List<PeerCatalogSummary> GetSummaries()
        {
            var counts = _items.CountByStatus();

            return _peers.All()
                .Where(p => p.State == PeerState.Trusted)
                .OrderBy(p => p.Name)
                .Select(p => new PeerCatalogSummary
                {
                    PeerId = p.Id,
                    PeerName = p.Name,
                    CatalogVersion = p.CatalogVersion,
                    CatalogSyncedAt = p.CatalogSyncedAt,
                    CatalogError = p.CatalogError,
                    NotInLibrary = counts.GetValueOrDefault((p.Id, PeerItemStatus.NotInLibrary)),
                    Missing = counts.GetValueOrDefault((p.Id, PeerItemStatus.Missing)),
                    Upgrade = counts.GetValueOrDefault((p.Id, PeerItemStatus.Upgrade)),
                    Have = counts.GetValueOrDefault((p.Id, PeerItemStatus.Have))
                })
                .ToList();
        }

        public PagingSpec<PeerCatalogItem> Paged(PagingSpec<PeerCatalogItem> pagingSpec)
        {
            return _items.GetPaged(pagingSpec);
        }

        public void HandleAsync(PeerStateChangedEvent message)
        {
            if (message.Peer.State == PeerState.Trusted)
            {
                Sync(message.Peer);
            }
            else
            {
                // A node that is no longer trusted keeps nothing here.
                Purge(message.Peer.Id);
            }
        }

        public void HandleAsync(PeerCatalogChangedEvent message)
        {
            Sync(message.PeerId);
        }

        public void HandleAsync(CatalogRefreshedEvent message)
        {
            var holdings = _holdings.Load();
            var changed = new List<PeerCatalogItem>();

            foreach (var item in _items.All())
            {
                var status = holdings.Classify(item);

                if (status != item.LocalStatus)
                {
                    item.LocalStatus = status;
                    changed.Add(item);
                }
            }

            _items.SetFields(changed, i => i.LocalStatus);
        }

        public void Handle(PeerDeletedEvent message)
        {
            _items.DeleteForPeer(message.PeerId);
        }

        private void Sync(Peer peer)
        {
            try
            {
                var offer = Fetch(peer);

                if (offer != null)
                {
                    var holdings = _holdings.Load();
                    var items = offer.Items
                        .Where(o => o.Id.IsNotNullOrWhiteSpace())
                        .DistinctBy(o => o.Id)
                        .Select(o => PeerCatalogItem.FromOffer(peer.Id, o))
                        .ToList();

                    foreach (var item in items)
                    {
                        item.LocalStatus = holdings.Classify(item);
                        item.CandidateId = _candidateIds.For(peer.NodeId, item.RemoteId);
                        item.ReleaseTitle = ReleaseTitleBuilder.For(item);
                    }

                    // The whole set is replaced, so what the peer stopped offering is gone.
                    _items.DeleteForPeer(peer.Id);
                    _items.InsertMany(items);
                    peer.CatalogVersion = offer.Version;

                    _logger.Debug("Catalog from {0} is now version {1} with {2} files", peer.Name, offer.Version, items.Count);
                }

                peer.CatalogSyncedAt = DateTime.UtcNow;
                peer.CatalogError = null;
            }
            catch (TransportException ex) when (ex.Failure is TransportFailure.Blocked or TransportFailure.NotAuthorized)
            {
                // The peer no longer shares with this node. What it offered before cannot be had.
                _items.DeleteForPeer(peer.Id);
                peer.CatalogVersion = null;
                peer.CatalogError = $"{ex.Failure}: {ex.Message}";
            }
            catch (Exception ex) when (ex is TransportException or PeerException)
            {
                // Unreachable for now. Keep what is known and say so.
                peer.CatalogError = ex.Message;
            }

            _peers.SetFields(peer, p => p.CatalogVersion, p => p.CatalogSyncedAt, p => p.CatalogError);
        }

        // Returns the peer's offer, or null when it has not changed since the last sync.
        private Offer Fetch(Peer peer)
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var first = _peerService.Ask(peer, new PeerMessage { Type = PeerMessageTypes.Catalog, CatalogKnownVersion = peer.CatalogVersion });

                if (first is not { Ok: true })
                {
                    throw new PeerException("The node did not answer the catalog request");
                }

                if (first.CatalogUnchanged)
                {
                    return null;
                }

                var offer = new Offer { Version = first.CatalogVersion, Items = first.CatalogItems ?? new List<OfferItem>() };
                var complete = true;

                for (var page = 1; page < Math.Min(first.CatalogTotalPages, MaxPages); page++)
                {
                    var next = _peerService.Ask(peer, new PeerMessage { Type = PeerMessageTypes.Catalog, CatalogPage = page, CatalogVersion = offer.Version });

                    if (next is not { Ok: true })
                    {
                        // The peer's offer changed between pages. Start over with the new one.
                        complete = false;
                        break;
                    }

                    offer.Items.AddRange(next.CatalogItems ?? new List<OfferItem>());
                }

                if (complete)
                {
                    return offer;
                }
            }

            throw new PeerException("The node's catalog kept changing while it was being read");
        }

        private void Sync(int peerId)
        {
            var peer = _peers.Find(peerId);

            if (peer is { State: PeerState.Trusted })
            {
                Sync(peer);
            }
        }

        private void Purge(int peerId)
        {
            _items.DeleteForPeer(peerId);

            var peer = _peers.Find(peerId);

            if (peer != null)
            {
                peer.CatalogVersion = null;
                peer.CatalogSyncedAt = null;
                peer.CatalogError = null;
                _peers.SetFields(peer, p => p.CatalogVersion, p => p.CatalogSyncedAt, p => p.CatalogError);
            }
        }
    }
}

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
                var fetched = Fetch(peer, peer.CatalogVersion, acceptChanges: true);

                if (fetched is { Changes: true } && !ApplyChanges(peer, fetched))
                {
                    // What this node holds does not add up to what the peer says it sent. Start
                    // again from the whole catalog.
                    _logger.Warn("Catalog changes from {0} did not add up to version {1}. Fetching the whole catalog.", peer.Name, fetched.Version);
                    fetched = Fetch(peer, null, acceptChanges: false);
                }

                if (fetched is { Changes: false })
                {
                    ApplyWhole(peer, fetched);
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

        // The whole set is replaced, so what the peer stopped offering is gone.
        private void ApplyWhole(Peer peer, Fetched fetched)
        {
            var items = ToItems(peer, fetched.Items);

            _items.DeleteForPeer(peer.Id);
            _items.InsertMany(items);
            peer.CatalogVersion = fetched.Version;

            // A peer on an older Nodearr sends no fingerprints, and there is nothing to check.
            if (items.All(i => i.Fingerprint != null) && Offer.VersionOf(items.Select(i => (i.RemoteId, i.Fingerprint))) != fetched.Version)
            {
                // Kept, since it is what the peer sent, but the next sync asks for all of it again.
                _logger.Warn("The catalog {0} sent does not add up to its version {1}", peer.Name, fetched.Version);
                peer.CatalogVersion = null;
            }

            _logger.Debug("Catalog from {0} is now version {1} with {2} files", peer.Name, fetched.Version, items.Count);
        }

        // Only what changed. Returns false when the result does not add up to the version the
        // peer says it is, which needs the whole catalog fetched.
        private bool ApplyChanges(Peer peer, Fetched fetched)
        {
            var items = ToItems(peer, fetched.Items);

            _items.DeleteForPeer(peer.Id, fetched.Removed.Concat(items.Select(i => i.RemoteId)));
            _items.InsertMany(items);

            var held = _items.FingerprintsForPeer(peer.Id);

            if (held.Count != fetched.ItemCount || held.Any(h => h.Fingerprint == null) || Offer.VersionOf(held) != fetched.Version)
            {
                return false;
            }

            peer.CatalogVersion = fetched.Version;
            _logger.Debug("Catalog from {0} is now version {1}: {2} files added or changed, {3} removed", peer.Name, fetched.Version, items.Count, fetched.Removed.Count);

            return true;
        }

        private List<PeerCatalogItem> ToItems(Peer peer, List<OfferItem> offered)
        {
            var holdings = _holdings.Load();
            var items = offered
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

            return items;
        }

        // The peer's catalog, or only what changed in it since knownVersion when the peer can say.
        // Null when nothing changed.
        private Fetched Fetch(Peer peer, string knownVersion, bool acceptChanges)
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var first = _peerService.Ask(peer, new PeerMessage { Type = PeerMessageTypes.Catalog, CatalogKnownVersion = knownVersion, CatalogAcceptsChanges = acceptChanges, CatalogAcceptsMusic = true });

                if (first is not { Ok: true })
                {
                    throw new PeerException("The node did not answer the catalog request");
                }

                if (first.CatalogUnchanged)
                {
                    return null;
                }

                var fetched = new Fetched
                {
                    Version = first.CatalogVersion,
                    Items = first.CatalogItems ?? new List<OfferItem>(),
                    Changes = acceptChanges && first.CatalogChanges,
                    Removed = first.CatalogRemoved ?? new List<string>(),
                    ItemCount = first.CatalogItemCount
                };
                var complete = true;

                for (var page = 1; page < Math.Min(first.CatalogTotalPages, MaxPages); page++)
                {
                    var next = _peerService.Ask(peer, new PeerMessage { Type = PeerMessageTypes.Catalog, CatalogPage = page, CatalogVersion = fetched.Version });

                    if (next is not { Ok: true })
                    {
                        // The peer's offer changed between pages. Start over with the new one.
                        complete = false;
                        break;
                    }

                    fetched.Items.AddRange(next.CatalogItems ?? new List<OfferItem>());
                }

                if (complete)
                {
                    return fetched;
                }
            }

            throw new PeerException("The node's catalog kept changing while it was being read");
        }

        private class Fetched
        {
            public string Version { get; set; }
            public List<OfferItem> Items { get; set; }
            public bool Changes { get; set; }
            public List<string> Removed { get; set; }
            public int ItemCount { get; set; }
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

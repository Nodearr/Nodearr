using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;

namespace NzbDrone.Core.Sharing
{
    public interface IOfferExchange
    {
        PeerReply Answer(Peer peer, PeerMessage request);
    }

    // Answers a trusted peer's request for this node's catalog, one page at a time.
    //
    // The first page builds the peer's offer afresh. If the peer holds the version this node last
    // sent it, and can take changes, it is sent only the files added or changed since and the IDs
    // of those removed. Otherwise it is sent the whole catalog. Either way the reply is remembered,
    // so the following pages come from the same snapshot even if the library changes in between. A
    // peer that asks for a later page of a snapshot that has been replaced is told to start again.
    //
    // A change to what is shared drops every snapshot, so a peer part way through is told to start
    // again too. Otherwise a title put on Hidden after the first page would still be in the later
    // pages, for as long as the peer took to ask for them.
    public class OfferExchange : IOfferExchange, IHandle<PeerDeletedEvent>, IHandle<SharingChangedEvent>
    {
        public const int PageSize = 500;

        private readonly ISharingService _sharingService;
        private readonly IOfferRecordRepository _records;
        private readonly ConcurrentDictionary<int, Snapshot> _snapshots = new();

        public OfferExchange(ISharingService sharingService, IOfferRecordRepository records)
        {
            _sharingService = sharingService;
            _records = records;
        }

        public PeerReply Answer(Peer peer, PeerMessage request)
        {
            Snapshot snapshot;

            if (request.CatalogPage <= 0)
            {
                var offer = _sharingService.BuildOffer(peer, request.CatalogAcceptsMusic);

                if (offer.Version == request.CatalogKnownVersion)
                {
                    // The peer holds what it would be sent. Make sure the next change can be sent
                    // as a change, even if this node lost its record of what the peer holds.
                    if (_records.FindForPeer(peer.Id)?.Version != offer.Version)
                    {
                        Remember(peer, offer);
                    }

                    return new PeerReply { Ok = true, CatalogVersion = offer.Version, CatalogUnchanged = true, CatalogItemCount = offer.Items.Count };
                }

                snapshot = Prepare(peer, offer, request);
                _snapshots[peer.Id] = snapshot;
                Remember(peer, offer);
            }
            else if (!_snapshots.TryGetValue(peer.Id, out snapshot) || snapshot.Version != request.CatalogVersion)
            {
                return new PeerReply { Ok = false, Error = "CatalogChanged" };
            }

            var page = Math.Max(request.CatalogPage, 0);

            return new PeerReply
            {
                Ok = true,
                CatalogVersion = snapshot.Version,
                CatalogTotalPages = Math.Max(1, (int)Math.Ceiling(snapshot.Items.Count / (double)PageSize)),
                CatalogItems = snapshot.Items.Skip(page * PageSize).Take(PageSize).ToList(),
                CatalogChanges = snapshot.Changes,
                CatalogRemoved = snapshot.Changes && page == 0 ? snapshot.Removed : null,
                CatalogItemCount = snapshot.ItemCount
            };
        }

        public void Handle(PeerDeletedEvent message)
        {
            _snapshots.TryRemove(message.PeerId, out _);
            _records.DeleteForPeer(message.PeerId);
        }

        public void Handle(SharingChangedEvent message)
        {
            _snapshots.Clear();
        }

        // Only what changed, when this node knows what the peer holds and the peer can take it.
        // When more than half the files changed, the whole catalog is about as big, and simpler.
        private Snapshot Prepare(Peer peer, Offer offer, PeerMessage request)
        {
            var whole = new Snapshot { Version = offer.Version, Items = offer.Items, ItemCount = offer.Items.Count };

            if (!request.CatalogAcceptsChanges || request.CatalogKnownVersion == null)
            {
                return whole;
            }

            var record = _records.FindForPeer(peer.Id);

            if (record?.Version != request.CatalogKnownVersion)
            {
                return whole;
            }

            var before = record.GetFingerprints();
            var changed = offer.Items.Where(i => !before.TryGetValue(i.Id, out var fingerprint) || fingerprint != i.Fingerprint).ToList();

            if (changed.Count > offer.Items.Count / 2 && changed.Count > PageSize)
            {
                return whole;
            }

            var current = offer.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

            return new Snapshot
            {
                Version = offer.Version,
                Items = changed,
                Changes = true,
                Removed = before.Keys.Where(id => !current.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                ItemCount = offer.Items.Count
            };
        }

        private void Remember(Peer peer, Offer offer)
        {
            _records.Save(new OfferRecord { PeerId = peer.Id, Version = offer.Version, Fingerprints = OfferRecord.Write(offer.Items), Sent = DateTime.UtcNow });
        }

        private class Snapshot
        {
            public string Version { get; set; }
            public List<OfferItem> Items { get; set; }
            public bool Changes { get; set; }
            public List<string> Removed { get; set; }
            public int ItemCount { get; set; }
        }
    }
}

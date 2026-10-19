using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Nodes
{
    public enum PeerState
    {
        // An invite was redeemed here and the other node has not confirmed yet.
        Pending = 0,

        // Both sides completed pairing.
        Trusted = 1,

        // Refused at the door. Nothing from this node is read, and it cannot pair again until unblocked.
        Blocked = 2,

        // Trust was withdrawn, here or by the other node. Pairing again needs a new invite.
        Revoked = 3
    }

    // Another Nodearr node, identified by its public key.
    public class Peer : ModelBase
    {
        public string NodeId { get; set; }
        public string Name { get; set; }
        public PeerState State { get; set; }
        public string StateReason { get; set; }
        public string RelayUrl { get; set; }
        public string IpAddresses { get; set; }
        public DateTime Added { get; set; }
        public DateTime StateChanged { get; set; }
        public DateTime? LastSeen { get; set; }
        public string LastError { get; set; }

        // What the last connection check found: whether a direct path to this node exists, which
        // is what a file transfer needs.
        public ConnectionPath ConnectionPath { get; set; }
        public string ConnectionAddress { get; set; }
        public DateTime? ConnectionChecked { get; set; }

        // Offer this node everything in the local catalog except what is on the Hidden list.
        public bool ShareAll { get; set; }

        // The Arr apps whose libraries "everything" leaves out for this node. Their titles can still
        // be shared with it on a list.
        public List<int> ExcludedApplicationIds { get; set; } = new();

        // The last catalog snapshot received from this node.
        public string CatalogVersion { get; set; }
        public DateTime? CatalogSyncedAt { get; set; }
        public string CatalogError { get; set; }

        public NodeAddress ToAddress()
        {
            return new NodeAddress
            {
                NodeId = NodeId,
                RelayUrl = RelayUrl,
                IpAddresses = (IpAddresses ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            };
        }

        public void SetAddress(string relayUrl, IEnumerable<string> ipAddresses)
        {
            // Keep what is known if the other side sent nothing useful.
            if (relayUrl.IsNotNullOrWhiteSpace())
            {
                RelayUrl = relayUrl;
            }

            var addresses = (ipAddresses ?? Enumerable.Empty<string>()).Where(a => a.IsNotNullOrWhiteSpace()).ToList();

            if (addresses.Count > 0)
            {
                IpAddresses = string.Join(",", addresses);
            }
        }
    }

    // A one-time, expiring permission to pair with this node. Only a hash of the secret is kept.
    public class PeerInvite : ModelBase
    {
        public string SecretHash { get; set; }
        public string Label { get; set; }
        public DateTime Created { get; set; }
        public DateTime Expires { get; set; }
        public DateTime? RedeemedAt { get; set; }
        public string RedeemedByNodeId { get; set; }

        // Whoever redeems this invite is offered everything except the Hidden list straight away.
        public bool ShareAll { get; set; }
    }

    // Raised so later slices can react: a blocked or revoked peer must lose its cached catalog and
    // have its transfers cancelled.
    public class PeerStateChangedEvent : IEvent
    {
        public PeerStateChangedEvent(Peer peer, PeerState previousState)
        {
            Peer = peer;
            PreviousState = previousState;
        }

        public Peer Peer { get; }
        public PeerState PreviousState { get; }
    }

    // Raised after a connection check, whatever it found.
    public class PeerConnectionCheckedEvent : IEvent
    {
        public PeerConnectionCheckedEvent(Peer peer)
        {
            Peer = peer;
        }

        public Peer Peer { get; }
    }

    // Raised on the inviting node when someone pairs with one of its invites.
    public class InviteRedeemedEvent : IEvent
    {
        public InviteRedeemedEvent(PeerInvite invite, Peer peer)
        {
            Invite = invite;
            Peer = peer;
        }

        public PeerInvite Invite { get; }
        public Peer Peer { get; }
    }

    public class PeerDeletedEvent : IEvent
    {
        public PeerDeletedEvent(int peerId)
        {
            PeerId = peerId;
        }

        public int PeerId { get; }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Nodes;

namespace NzbDrone.Core.Sharing
{
    // Something sent to a friend: shared with them on a list of its own, and their node told, so it
    // can offer to fetch it. A send to someone new waits for their invite to be used.
    public class SentTitle : ModelBase
    {
        // What both nodes know this send by.
        public string SendId { get; set; }

        // The friend, or, until they use it, the invite they were sent.
        public int? PeerId { get; set; }
        public int? InviteId { get; set; }

        // As the sender's library names it, and which part of it, such as "Seasons 1 and 2".
        public string Title { get; set; }
        public string Summary { get; set; }
        public MediaKind MediaKind { get; set; }

        // The share keys that were sent, as JSON.
        public string Items { get; set; }
        public DateTime Created { get; set; }

        // When the friend's node was told. Until then it is tried again whenever the two connect.
        public DateTime? DeliveredAt { get; set; }
        public string LastError { get; set; }

        public List<ShareKey> GetItems()
        {
            return Items.IsNullOrWhiteSpace() ? new List<ShareKey>() : JsonSerializer.Deserialize<List<ShareKey>>(Items);
        }
    }

    // Something a friend sent this node. The Get it card is built from it.
    public class ReceivedTitle : ModelBase
    {
        public int PeerId { get; set; }
        public string SendId { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public MediaKind MediaKind { get; set; }

        // The whole title was sent, so what arrives in it later is meant too.
        public bool IsWhole { get; set; }

        // The friend's names for the files it covers, as in their offer, comma separated.
        public string OfferIds { get; set; }
        public DateTime Received { get; set; }

        // Set when the person acts on it or puts it away.
        public DateTime? Handled { get; set; }
    }

    // What a node says when it sends something, on the control channel.
    public class SentMessage
    {
        public string SendId { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public MediaKind MediaKind { get; set; }
        public bool IsWhole { get; set; }
        public List<string> OfferIds { get; set; } = new();
    }

    // Raised on the receiving node when a trusted friend says it sent something.
    public class TitlesSentEvent : IEvent
    {
        public TitlesSentEvent(Peer peer, SentMessage sent)
        {
            Peer = peer;
            Sent = sent;
        }

        public Peer Peer { get; }
        public SentMessage Sent { get; }
    }

    public class SentTitlesChangedEvent : IEvent
    {
    }

    // A send is ready to be delivered. After a pairing the other node needs a moment to finish
    // its side before it will listen.
    public class SendQueuedEvent : IEvent
    {
        public SendQueuedEvent(int sentTitleId, bool afterPairing = false)
        {
            SentTitleId = sentTitleId;
            AfterPairing = afterPairing;
        }

        public int SentTitleId { get; }
        public bool AfterPairing { get; }
    }

    public class ReceivedTitlesChangedEvent : IEvent
    {
    }
}

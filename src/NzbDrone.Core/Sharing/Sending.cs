using System;
using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Applications;
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

        // Set when the person puts it away, or a successful Get it no longer needs the card.
        public DateTime? Handled { get; set; }

        // What the Arr apps know the title by, so it can be added (Get it).
        public int Year { get; set; }
        public int TvdbId { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string ForeignArtistId { get; set; }
        public bool IsAdult { get; set; }
        public string SeriesType { get; set; }

        // Whisparr's own ID for a film or a scene, and StashDB's and ThePornDB's.
        public string ForeignId { get; set; }
        public string StashId { get; set; }
        public string TpdbId { get; set; }

        // Which seasons, episodes or albums were sent, as JSON. Empty for a whole title.
        public string Parts { get; set; }

        public GetItState GetItState { get; set; }
        public string GetItMessage { get; set; }
        public DateTime? GetItCompletedAt { get; set; }

        // The first time Home visibly showed an already-held notification. Polling does not count.
        public DateTime? AlreadyHeldSeenAt { get; set; }

        // Recomputed from the current catalogs, never stored with the sender's note.
        public ReceivedAvailability Availability { get; set; } = new();

        public SentParts GetParts()
        {
            return Parts.IsNullOrWhiteSpace() ? new SentParts() : JsonSerializer.Deserialize<SentParts>(Parts) ?? new SentParts();
        }
    }

    public enum GetItState
    {
        None,
        Working,
        Done,
        Failed
    }

    public class ReceivedAvailability
    {
        public bool AlreadyHaveIt { get; set; }
        public int? NewFiles { get; set; }
        public int? UpgradeFiles { get; set; }
    }

    // The parts of a title that were sent, by the numbers and IDs every node's Arr apps share:
    // TVDB's seasons and episodes, and MusicBrainz release groups.
    public class SentParts
    {
        public List<int> Seasons { get; set; } = new();
        public List<SeasonEpisode> Episodes { get; set; } = new();
        public List<string> Albums { get; set; } = new();
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

        // What the Arr apps know the title by, and which parts of it were sent, so the friend's
        // node can add it to their app. An older sender says none of this.
        public int Year { get; set; }
        public int TvdbId { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string ForeignArtistId { get; set; }
        public bool IsAdult { get; set; }
        public string SeriesType { get; set; }
        public string ForeignId { get; set; }
        public string StashId { get; set; }
        public string TpdbId { get; set; }
        public SentParts Parts { get; set; }
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

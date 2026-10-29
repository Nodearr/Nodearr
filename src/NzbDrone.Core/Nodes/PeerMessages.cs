using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Nodes
{
    public static class PeerMessageTypes
    {
        public const string Pair = "pair";
        public const string Ping = "ping";
        public const string Revoke = "revoke";
        public const string Catalog = "catalog";
        public const string CatalogChanged = "catalogChanged";

        // A friend sent something to this node. An older node does not know it, and refuses it.
        public const string Sent = "sent";

        // The poster of the title an offered item belongs to. An older node refuses it too.
        public const string Poster = "poster";

        // What a library holds may not cross a relay, as files may not: a relay would see how much
        // two nodes exchange, and when. These messages and their answers wait for a direct path.
        // Pairing, pings and revoking may cross one: finding a path, and saying there is none,
        // needs them (ADR 0011).
        public static bool NeedsDirect(string type)
        {
            return type is Catalog or CatalogChanged or Poster or Sent;
        }
    }

    // What one node says to another on the control channel. The transport has already verified
    // who sent it, so the message never states the sender's node ID.
    public class PeerMessage
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public int Version { get; set; } = 1;
        public string Type { get; set; }

        // How the sender likes to be called, and where it can currently be reached.
        public string SenderName { get; set; }
        public string SenderRelayUrl { get; set; }
        public List<string> SenderIpAddresses { get; set; } = new();

        // Pairing only.
        public string InviteSecret { get; set; }

        // Catalog only: the version the asker already holds, the page it wants, and for pages after
        // the first, the version that page must belong to.
        public string CatalogKnownVersion { get; set; }
        public int CatalogPage { get; set; }
        public string CatalogVersion { get; set; }

        // The asker can take only what changed since CatalogKnownVersion. An older node does not
        // say so, and is always sent the whole catalog.
        public bool CatalogAcceptsChanges { get; set; }

        // The asker knows albums (ADR 0020). An older node does not say so, and is sent none.
        public bool CatalogAcceptsMusic { get; set; }

        // Sent only.
        public SentMessage Sent { get; set; }

        // Poster only: the item of this node's offer whose title's poster is wanted.
        public string PosterFor { get; set; }

        public byte[] ToBytes()
        {
            return JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        }

        public static PeerMessage FromBytes(byte[] bytes)
        {
            try
            {
                return JsonSerializer.Deserialize<PeerMessage>(bytes, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public class PeerReply
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public bool Ok { get; set; }
        public string Error { get; set; }
        public string Name { get; set; }

        // Catalog only.
        public string CatalogVersion { get; set; }
        public bool CatalogUnchanged { get; set; }
        public int CatalogTotalPages { get; set; }
        public List<OfferItem> CatalogItems { get; set; }

        // Set when the pages hold only what changed since the version the asker holds: the files
        // added or changed, and on the first page the IDs of the files no longer offered.
        public bool CatalogChanges { get; set; }
        public List<string> CatalogRemoved { get; set; }

        // How many files the whole catalog at CatalogVersion has, so the asker can check.
        public int CatalogItemCount { get; set; }

        // The poster, as a JPEG or a PNG. None when the title has none.
        public byte[] Poster { get; set; }

        public byte[] ToBytes()
        {
            return JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        }

        public static PeerReply FromBytes(byte[] bytes)
        {
            try
            {
                return JsonSerializer.Deserialize<PeerReply>(bytes, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

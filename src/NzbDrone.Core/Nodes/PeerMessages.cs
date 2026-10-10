using System.Collections.Generic;
using System.Text.Json;

namespace NzbDrone.Core.Nodes
{
    public static class PeerMessageTypes
    {
        public const string Pair = "pair";
        public const string Ping = "ping";
        public const string Revoke = "revoke";
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

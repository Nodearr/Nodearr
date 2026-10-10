using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Nodes
{
    // The text one person sends another to pair their nodes. It names the inviting node, says where
    // to reach it, and carries a one-time secret that proves the holder was invited.
    public class InviteCode
    {
        public const string Prefix = "nodearr-invite:";

        [JsonPropertyName("v")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("n")]
        public string NodeId { get; set; }

        [JsonPropertyName("r")]
        public string RelayUrl { get; set; }

        [JsonPropertyName("a")]
        public List<string> IpAddresses { get; set; } = new();

        [JsonPropertyName("s")]
        public string Secret { get; set; }

        [JsonPropertyName("e")]
        public long ExpiresUnix { get; set; }

        [JsonIgnore]
        public DateTime Expires => DateTimeOffset.FromUnixTimeSeconds(ExpiresUnix).UtcDateTime;

        public static string NewSecret()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        public static string HashSecret(string secret)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret ?? string.Empty)));
        }

        public NodeAddress ToAddress()
        {
            return new NodeAddress { NodeId = NodeId, RelayUrl = RelayUrl, IpAddresses = IpAddresses ?? new List<string>() };
        }

        public string Encode()
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(this);

            return Prefix + Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static bool TryDecode(string text, out InviteCode code)
        {
            code = null;
            text = text?.Trim();

            if (string.IsNullOrEmpty(text) || !text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var base64 = text[Prefix.Length..].Replace('-', '+').Replace('_', '/');

                base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
                code = JsonSerializer.Deserialize<InviteCode>(Convert.FromBase64String(base64));

                return code is { Version: 1 } && !string.IsNullOrWhiteSpace(code.NodeId) && !string.IsNullOrWhiteSpace(code.Secret);
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                code = null;

                return false;
            }
        }
    }
}

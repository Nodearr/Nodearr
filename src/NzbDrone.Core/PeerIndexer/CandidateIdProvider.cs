using System;
using System.Security.Cryptography;
using System.Text;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.PeerIndexer
{
    public interface ICandidateIdProvider
    {
        string For(string peerNodeId, string remoteId);
    }

    // The name an Arr app knows a peer's file by. It is a keyed hash of the peer and the peer's own
    // name for the file, so it stays the same for as long as the peer offers the file, says nothing
    // about either to whoever reads it, and cannot be made up without this node's secret.
    public class CandidateIdProvider : ICandidateIdProvider
    {
        private readonly Lazy<byte[]> _key;

        public CandidateIdProvider(IConfigService configService)
        {
            // Read once. The secret is created on first use, and two first uses must not disagree.
            _key = new Lazy<byte[]>(() => Convert.FromHexString(configService.CandidateIdSecret));
        }

        public string For(string peerNodeId, string remoteId)
        {
            var mac = HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes(peerNodeId + "\n" + remoteId));

            return Convert.ToHexString(mac, 0, 16).ToLowerInvariant();
        }
    }
}

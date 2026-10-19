using Nodearr.Http.REST;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;

namespace Nodearr.Api.V5.Nodes;

public class NodeIdentityResource
{
    public bool TransportRunning { get; set; }
    public string? NodeId { get; set; }
    public string? Name { get; set; }
    public string? RelayUrl { get; set; }
    public List<string> IpAddresses { get; set; } = new();

    // What the transport has worked out about this node's network. Each is absent until known.
    public bool? Udp { get; set; }
    public bool? MappingVaries { get; set; }
    public string? PublicAddress { get; set; }
    public bool? CaptivePortal { get; set; }

    // In a container on Docker Desktop, where a varying public port is Docker's doing.
    public bool DockerDesktop { get; set; }

    public static NodeIdentityResource FromModel(NodeIdentity identity)
    {
        return new NodeIdentityResource
        {
            TransportRunning = identity.TransportRunning,
            NodeId = identity.NodeId,
            Name = identity.Name,
            RelayUrl = identity.RelayUrl,
            IpAddresses = identity.IpAddresses,
            Udp = identity.Network?.Udp,
            MappingVaries = identity.Network?.MappingVaries,
            PublicAddress = identity.Network?.PublicAddress,
            CaptivePortal = identity.Network?.CaptivePortal,
            DockerDesktop = identity.DockerDesktop
        };
    }
}

public class PeerResource : RestResource
{
    public string? NodeId { get; set; }
    public string? Name { get; set; }
    public PeerState State { get; set; }
    public string? StateReason { get; set; }
    public DateTime Added { get; set; }
    public DateTime StateChanged { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? LastError { get; set; }
    public ConnectionPath ConnectionPath { get; set; }
    public string? ConnectionAddress { get; set; }
    public DateTime? ConnectionChecked { get; set; }
    public bool ShareAll { get; set; }

    // The Arr apps that everything leaves out for this friend.
    public List<int> ExcludedApplicationIds { get; set; } = new();

    public static PeerResource FromModel(Peer peer)
    {
        return new PeerResource
        {
            Id = peer.Id,
            NodeId = peer.NodeId,
            Name = peer.Name,
            State = peer.State,
            StateReason = peer.StateReason,
            Added = peer.Added,
            StateChanged = peer.StateChanged,
            LastSeen = peer.LastSeen,
            LastError = peer.LastError,
            ConnectionPath = peer.ConnectionPath,
            ConnectionAddress = peer.ConnectionAddress,
            ConnectionChecked = peer.ConnectionChecked,
            ShareAll = peer.ShareAll,
            ExcludedApplicationIds = peer.ExcludedApplicationIds ?? new List<int>()
        };
    }
}

public class InviteResource
{
    public int Id { get; set; }
    public string? Label { get; set; }
    public DateTime Created { get; set; }
    public DateTime Expires { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public string? RedeemedByNodeId { get; set; }

    public bool ShareAll { get; set; }

    // Present only in the answer to creating the invite.
    public string? Code { get; set; }

    public static InviteResource FromModel(PeerInvite invite, string? code = null)
    {
        return new InviteResource
        {
            Id = invite.Id,
            Label = invite.Label,
            Created = invite.Created,
            Expires = invite.Expires,
            RedeemedAt = invite.RedeemedAt,
            RedeemedByNodeId = invite.RedeemedByNodeId,
            ShareAll = invite.ShareAll,
            Code = code
        };
    }
}

public class CreateInviteResource
{
    public int ValidHours { get; set; } = 24;
    public string? Label { get; set; }

    // Offer whoever redeems the invite everything except the Hidden list. On unless turned off.
    public bool ShareAll { get; set; } = true;
}

public class ShareAllResource
{
    public bool ShareAll { get; set; }

    // The Arr apps everything leaves out. Left out of the request, they stay as they are.
    public List<int>? ExcludedApplicationIds { get; set; }
}

public class RedeemInviteResource
{
    // Offer the new friend everything except Hidden. On unless turned off.
    public bool ShareAll { get; set; } = true;

    public string? Code { get; set; }
}

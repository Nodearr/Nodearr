using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;
using NzbDrone.SignalR;

namespace Nodearr.Api.V5.Nodes;

public abstract class NodeControllerBase : Controller
{
    // Peer and transport failures are things the user can act on, so they go back as 400 with the reason.
    protected static T Run<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is PeerException or TransportException)
        {
            throw new BadRequestException(ex.Message);
        }
    }
}

[V5ApiController("nodes/identity")]
public class NodeIdentityController : NodeControllerBase
{
    private readonly IPeerService _peerService;

    public NodeIdentityController(IPeerService peerService)
    {
        _peerService = peerService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<NodeIdentityResource> GetIdentity()
    {
        return TypedResults.Ok(NodeIdentityResource.FromModel(Run(_peerService.GetIdentity)));
    }
}

[V5ApiController("nodes/peers")]
public class PeerController : NodeControllerBase, IHandle<PeerConnectionCheckedEvent>, IHandle<PeerStateChangedEvent>
{
    private readonly IPeerService _peerService;
    private readonly ISharingService _sharingService;
    private readonly IBroadcastSignalRMessage _broadcaster;

    public PeerController(IPeerService peerService, ISharingService sharingService, IBroadcastSignalRMessage broadcaster)
    {
        _peerService = peerService;
        _sharingService = sharingService;
        _broadcaster = broadcaster;
    }

    // Connections are checked without anyone asking, and nodes pair from the other side. Open
    // pages are told to fetch the list again.
    [NonAction]
    public void Handle(PeerConnectionCheckedEvent message)
    {
        TellPages();
    }

    [NonAction]
    public void Handle(PeerStateChangedEvent message)
    {
        TellPages();
    }

    private void TellPages()
    {
        if (!_broadcaster.IsConnected)
        {
            return;
        }

        _broadcaster.BroadcastMessage(new SignalRMessage
        {
            Name = "nodes",
            Body = new ResourceChangeMessage<PeerResource>(ModelAction.Sync),
            Action = ModelAction.Sync,
            Version = 5
        });
    }

    [HttpPut("{id:int}/shareall")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<PeerResource> SetShareAll(int id, [FromBody] ShareAllResource resource)
    {
        return TypedResults.Ok(PeerResource.FromModel(_sharingService.SetShareAll(id, resource.ShareAll, resource.ExcludedApplicationIds)));
    }

    [HttpPut("{id:int}/automaticfetch")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<PeerResource> SetAutomaticFetch(int id, [FromBody] AutomaticFetchResource resource)
    {
        var peer = Run(() => _peerService.SetAutomaticFetch(id, resource.FetchNewFilesAutomatically));
        TellPages();
        return TypedResults.Ok(PeerResource.FromModel(peer));
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<PeerResource>> GetPeers()
    {
        return TypedResults.Ok(_peerService.All().ConvertAll(PeerResource.FromModel));
    }

    [HttpPost("{id:int}/ping")]
    [Produces("application/json")]
    public Ok<PeerResource> Ping(int id)
    {
        return TypedResults.Ok(PeerResource.FromModel(Run(() => _peerService.Ping(id))));
    }

    [HttpPost("{id:int}/block")]
    [Produces("application/json")]
    public Ok<PeerResource> Block(int id)
    {
        return TypedResults.Ok(PeerResource.FromModel(Run(() => _peerService.Block(id))));
    }

    [HttpPost("{id:int}/unblock")]
    [Produces("application/json")]
    public Ok<PeerResource> Unblock(int id)
    {
        return TypedResults.Ok(PeerResource.FromModel(Run(() => _peerService.Unblock(id))));
    }

    [HttpPost("{id:int}/revoke")]
    [Produces("application/json")]
    public Ok<PeerResource> Revoke(int id)
    {
        return TypedResults.Ok(PeerResource.FromModel(Run(() => _peerService.Revoke(id))));
    }

    [HttpDelete("{id:int}")]
    public NoContent Delete(int id)
    {
        Run(() =>
        {
            _peerService.Delete(id);

            return true;
        });

        return TypedResults.NoContent();
    }
}

[V5ApiController("nodes/invites")]
public class InviteController : NodeControllerBase
{
    private readonly IPeerService _peerService;

    public InviteController(IPeerService peerService)
    {
        _peerService = peerService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<InviteResource>> GetInvites()
    {
        return TypedResults.Ok(_peerService.Invites().ConvertAll(i => InviteResource.FromModel(i)));
    }

    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<InviteResource> Create([FromBody] CreateInviteResource resource)
    {
        if (resource.ValidHours is < 1 or > 24 * 7)
        {
            throw new BadRequestException("An invite can be valid for 1 hour to 7 days");
        }

        var created = Run(() => _peerService.CreateInvite(TimeSpan.FromHours(resource.ValidHours), resource.Label, resource.ShareAll));

        return TypedResults.Ok(InviteResource.FromModel(created.Invite, created.Code));
    }

    [HttpPost("redeem")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<PeerResource> Redeem([FromBody] RedeemInviteResource resource)
    {
        return TypedResults.Ok(PeerResource.FromModel(Run(() => _peerService.RedeemInvite(resource.Code ?? string.Empty, resource.ShareAll))));
    }

    [HttpDelete("{id:int}")]
    public NoContent Delete(int id)
    {
        _peerService.DeleteInvite(id);

        return TypedResults.NoContent();
    }
}

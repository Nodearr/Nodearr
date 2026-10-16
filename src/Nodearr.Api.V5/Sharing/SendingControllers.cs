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

namespace Nodearr.Api.V5.Sharing;

public enum SendStatus
{
    // Sent with an invite nobody has used yet.
    WaitingForInvite,

    // Sent with an invite that expired or was deleted unused.
    InviteLapsed,

    // Shared, and the friend's node not told yet.
    NotTold,
    Told
}

public class SentTitleResource : RestResource
{
    public int? PeerId { get; set; }
    public string? PeerName { get; set; }
    public int? InviteId { get; set; }
    public DateTime? InviteExpires { get; set; }
    public SendStatus Status { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public MediaKind MediaKind { get; set; }
    public List<ShareKeyResource> Items { get; set; } = new();
    public DateTime Created { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? LastError { get; set; }

    // Present only in the answer to sending with an invite.
    public string? InviteCode { get; set; }

    public static SentTitleResource FromModel(SentTitle sent, Dictionary<int, Peer> peers, Dictionary<int, PeerInvite> invites, string? inviteCode = null)
    {
        var peer = sent.PeerId.HasValue ? peers.GetValueOrDefault(sent.PeerId.Value) : null;
        var invite = sent.InviteId.HasValue ? invites.GetValueOrDefault(sent.InviteId.Value) : null;

        SendStatus status;

        if (sent.PeerId == null)
        {
            status = invite != null && invite.Expires > DateTime.UtcNow ? SendStatus.WaitingForInvite : SendStatus.InviteLapsed;
        }
        else
        {
            status = sent.DeliveredAt.HasValue ? SendStatus.Told : SendStatus.NotTold;
        }

        return new SentTitleResource
        {
            Id = sent.Id,
            PeerId = sent.PeerId,
            PeerName = peer?.Name,
            InviteId = sent.InviteId,
            InviteExpires = invite?.Expires,
            Status = status,
            Title = sent.Title,
            Summary = sent.Summary,
            MediaKind = sent.MediaKind,
            Items = sent.GetItems().ConvertAll(ShareKeyResource.FromModel),
            Created = sent.Created,
            DeliveredAt = sent.DeliveredAt,
            LastError = sent.LastError,
            InviteCode = inviteCode
        };
    }
}

public class CreateSendResource
{
    // A friend, or none for someone new, who is sent an invite.
    public int? PeerId { get; set; }
    public List<ShareKeyResource> Items { get; set; } = new();

    // How long the invite is good for, when there is one.
    public int ValidHours { get; set; } = 72;
}

public class ReceivedTitleResource : RestResource
{
    public int PeerId { get; set; }
    public string? PeerName { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public MediaKind MediaKind { get; set; }
    public bool IsWhole { get; set; }
    public List<string> OfferIds { get; set; } = new();
    public DateTime Received { get; set; }

    public static ReceivedTitleResource FromModel(ReceivedTitle received, Dictionary<int, Peer> peers)
    {
        return new ReceivedTitleResource
        {
            Id = received.Id,
            PeerId = received.PeerId,
            PeerName = peers.GetValueOrDefault(received.PeerId)?.Name,
            Title = received.Title,
            Summary = received.Summary,
            MediaKind = received.MediaKind,
            IsWhole = received.IsWhole,
            OfferIds = string.IsNullOrEmpty(received.OfferIds) ? new List<string>() : received.OfferIds.Split(',').ToList(),
            Received = received.Received
        };
    }
}

// Sending a title to a friend, or to someone new with an invite, and what has been sent.
[V5ApiController("sharing/sends")]
public class SendController : Controller, IHandle<SentTitlesChangedEvent>, IHandle<ReceivedTitlesChangedEvent>
{
    private readonly ISendService _sendService;
    private readonly IReceivedService _receivedService;
    private readonly IPeerService _peerService;
    private readonly IBroadcastSignalRMessage _broadcaster;

    public SendController(ISendService sendService, IReceivedService receivedService, IPeerService peerService, IBroadcastSignalRMessage broadcaster)
    {
        _sendService = sendService;
        _receivedService = receivedService;
        _peerService = peerService;
        _broadcaster = broadcaster;
    }

    [NonAction]
    public void Handle(SentTitlesChangedEvent message)
    {
        TellPages();
    }

    [NonAction]
    public void Handle(ReceivedTitlesChangedEvent message)
    {
        TellPages();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<SentTitleResource>> GetSends()
    {
        var peers = Peers();
        var invites = Invites();

        return TypedResults.Ok(_sendService.All().ConvertAll(s => SentTitleResource.FromModel(s, peers, invites)));
    }

    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<SentTitleResource> Send([FromBody] CreateSendResource resource)
    {
        var items = resource.Items.Select(i => i.ToModel()).ToList();

        try
        {
            if (resource.PeerId.HasValue)
            {
                var sent = _sendService.Send(resource.PeerId.Value, items);

                return TypedResults.Ok(SentTitleResource.FromModel(sent, Peers(), Invites()));
            }

            if (resource.ValidHours is < 1 or > 24 * 7)
            {
                throw new BadRequestException("An invite can be valid for 1 hour to 7 days");
            }

            var result = _sendService.SendWithInvite(items, TimeSpan.FromHours(resource.ValidHours));

            return TypedResults.Ok(SentTitleResource.FromModel(result.Sent, Peers(), Invites(), result.Invite.Code));
        }
        catch (Exception ex) when (ex is SendException or PeerException or TransportException)
        {
            throw new BadRequestException(ex.Message);
        }
    }

    [HttpDelete("{id:int}")]
    public NoContent Delete(int id)
    {
        _sendService.Delete(id);

        return TypedResults.NoContent();
    }

    // What friends sent this node, waiting to be acted on.
    [HttpGet("received")]
    [Produces("application/json")]
    public Ok<List<ReceivedTitleResource>> GetReceived()
    {
        var peers = Peers();

        return TypedResults.Ok(_receivedService.Waiting().ConvertAll(r => ReceivedTitleResource.FromModel(r, peers)));
    }

    [HttpPost("received/{id:int}/dismiss")]
    public NoContent Dismiss(int id)
    {
        _receivedService.Dismiss(id);

        return TypedResults.NoContent();
    }

    private Dictionary<int, Peer> Peers()
    {
        return _peerService.All().ToDictionary(p => p.Id);
    }

    private Dictionary<int, PeerInvite> Invites()
    {
        return _peerService.Invites().ToDictionary(i => i.Id);
    }

    private void TellPages()
    {
        if (!_broadcaster.IsConnected)
        {
            return;
        }

        _broadcaster.BroadcastMessage(new SignalRMessage
        {
            Name = "sending",
            Body = new ResourceChangeMessage<SentTitleResource>(ModelAction.Sync),
            Action = ModelAction.Sync,
            Version = 5
        });
    }
}

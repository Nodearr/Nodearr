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

    // Whether whoever uses the invite is offered everything except Hidden too. On unless turned off.
    public bool ShareAll { get; set; } = true;
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
    public int Year { get; set; }
    public GetItState GetItState { get; set; }
    public string? GetItMessage { get; set; }

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
            Received = received.Received,
            Year = received.Year,
            GetItState = received.GetItState,
            GetItMessage = received.GetItMessage
        };
    }
}

public class GetItOptionsResource
{
    public int ApplicationId { get; set; }
    public string? RootFolderPath { get; set; }
    public int QualityProfileId { get; set; }
    public int? MetadataProfileId { get; set; }
    public string? SeriesType { get; set; }
    public bool SeasonFolder { get; set; } = true;

    public static GetItOptionsResource? FromModel(GetItOptions? options)
    {
        return options == null ? null : new GetItOptionsResource
        {
            ApplicationId = options.ApplicationId,
            RootFolderPath = options.RootFolderPath,
            QualityProfileId = options.QualityProfileId,
            MetadataProfileId = options.MetadataProfileId,
            SeriesType = options.SeriesType,
            SeasonFolder = options.SeasonFolder
        };
    }

    public GetItOptions ToModel()
    {
        return new GetItOptions
        {
            ApplicationId = ApplicationId,
            RootFolderPath = RootFolderPath,
            QualityProfileId = QualityProfileId,
            MetadataProfileId = MetadataProfileId,
            SeriesType = SeriesType,
            SeasonFolder = SeasonFolder
        };
    }
}

public class GetItRootFolderResource
{
    public string? Path { get; set; }
    public long? FreeSpace { get; set; }
}

public class GetItProfileResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public class GetItTargetResource
{
    public int ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string? Implementation { get; set; }
    public bool InLibrary { get; set; }
    public List<GetItRootFolderResource> RootFolders { get; set; } = new();
    public List<GetItProfileResource> QualityProfiles { get; set; } = new();
    public List<GetItProfileResource> MetadataProfiles { get; set; } = new();
    public GetItOptionsResource? Defaults { get; set; }
    public string? Problem { get; set; }
}

// Where what was sent can be added, and how, filled in the way most of the library already is.
public class GetItPlanResource
{
    public List<GetItTargetResource> Targets { get; set; } = new();
    public string? Problem { get; set; }

    public static GetItPlanResource FromModel(GetItPlan plan)
    {
        return new GetItPlanResource
        {
            Problem = plan.Problem,
            Targets = plan.Targets.ConvertAll(t => new GetItTargetResource
            {
                ApplicationId = t.ApplicationId,
                ApplicationName = t.ApplicationName,
                Implementation = t.Implementation,
                InLibrary = t.InLibrary,
                RootFolders = t.Choices?.RootFolders.ConvertAll(f => new GetItRootFolderResource { Path = f.Path, FreeSpace = f.FreeSpace }) ?? new(),
                QualityProfiles = t.Choices?.QualityProfiles.ConvertAll(p => new GetItProfileResource { Id = p.Id, Name = p.Name }) ?? new(),
                MetadataProfiles = t.Choices?.MetadataProfiles.ConvertAll(p => new GetItProfileResource { Id = p.Id, Name = p.Name }) ?? new(),
                Defaults = GetItOptionsResource.FromModel(t.Defaults),
                Problem = t.Problem
            })
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
    private readonly IGetItService _getItService;
    private readonly IBroadcastSignalRMessage _broadcaster;

    public SendController(ISendService sendService, IReceivedService receivedService, IPeerService peerService, IGetItService getItService, IBroadcastSignalRMessage broadcaster)
    {
        _getItService = getItService;
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

            var result = _sendService.SendWithInvite(items, TimeSpan.FromHours(resource.ValidHours), resource.ShareAll);

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

    // Where what was sent can be added, and the choices for adding it.
    [HttpGet("received/{id:int}/getit")]
    [Produces("application/json")]
    public Ok<GetItPlanResource> GetItPlan(int id)
    {
        return TypedResults.Ok(GetItPlanResource.FromModel(_getItService.Plan(id)));
    }

    // Adds what was sent to the Arr app if it is missing, monitors it, and searches, in the background.
    [HttpPost("received/{id:int}/getit")]
    [Consumes("application/json")]
    public NoContent GetIt(int id, [FromBody] GetItOptionsResource resource)
    {
        try
        {
            _getItService.GetIt(id, resource.ToModel());
        }
        catch (GetItException ex)
        {
            throw new BadRequestException(ex.Message);
        }

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

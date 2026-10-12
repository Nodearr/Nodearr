using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Transfers;
using NzbDrone.SignalR;

namespace Nodearr.Api.V5.Transfers;

public class TransferResource : RestResource
{
    public string? DownloadId { get; set; }
    public string? Title { get; set; }
    public MediaKind MediaKind { get; set; }
    public string? Category { get; set; }
    public int PeerId { get; set; }
    public string? PeerName { get; set; }
    public TransferState State { get; set; }
    public TransferFailure Failure { get; set; }
    public string? FailureMessage { get; set; }
    public long Size { get; set; }
    public long BytesDone { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttempt { get; set; }
    public string? OutputPath { get; set; }
    public DateTime Added { get; set; }
    public DateTime? Started { get; set; }
    public DateTime? Ended { get; set; }
}

public class UploadResource
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? PeerName { get; set; }
    public UploadState State { get; set; }
    public string? Failure { get; set; }
    public string? FailureMessage { get; set; }
    public long Size { get; set; }
    public long BytesSent { get; set; }
    public DateTime Started { get; set; }
    public DateTime? Ended { get; set; }
}

[V5ApiController("transfers")]
public class TransferController : Controller, IHandle<TransfersChangedEvent>
{
    private readonly ITransferService _transfers;
    private readonly IUploadTracker _uploads;
    private readonly IPeerRepository _peers;
    private readonly IBroadcastSignalRMessage _broadcaster;

    public TransferController(ITransferService transfers, IUploadTracker uploads, IPeerRepository peers, IBroadcastSignalRMessage broadcaster)
    {
        _transfers = transfers;
        _uploads = uploads;
        _peers = peers;
        _broadcaster = broadcaster;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<TransferResource>> GetTransfers()
    {
        var names = _peers.All().ToDictionary(p => p.Id, p => p.Name);

        return TypedResults.Ok(_transfers.All().ConvertAll(job => new TransferResource
        {
            Id = job.Id,
            DownloadId = job.DownloadId,
            Title = job.Title,
            MediaKind = job.MediaKind,
            Category = job.Category,
            PeerId = job.PeerId,
            PeerName = names.GetValueOrDefault(job.PeerId),
            State = job.State,
            Failure = job.Failure,
            FailureMessage = job.FailureMessage,
            Size = job.Size,
            BytesDone = job.BytesDone,
            Attempts = job.Attempts,
            NextAttempt = job.NextAttempt,
            OutputPath = job.OutputPath,
            Added = job.Added,
            Started = job.Started,
            Ended = job.Ended
        }));
    }

    // What this node is sending to other nodes, and what it has sent since it started.
    [HttpGet("uploads")]
    [Produces("application/json")]
    public Ok<List<UploadResource>> GetUploads()
    {
        return TypedResults.Ok(_uploads.All().ConvertAll(upload => new UploadResource
        {
            Id = upload.Id,
            Title = upload.Title,
            PeerName = upload.PeerName,
            State = upload.State,
            Failure = upload.Failure,
            FailureMessage = upload.FailureMessage,
            Size = upload.Size,
            BytesSent = upload.BytesSent,
            Started = upload.Started,
            Ended = upload.Ended
        }));
    }

    // Removes a transfer, stopping it first if it is under way. The Arr app that asked for it
    // will see that it has gone.
    [HttpDelete("{downloadId}")]
    public Results<NoContent, NotFound> Remove(string downloadId, [FromQuery] bool deleteFiles = true)
    {
        return _transfers.Remove(downloadId, deleteFiles) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    [HttpPost("{downloadId}/retry")]
    [Produces("application/json")]
    public Ok<string> Retry(string downloadId)
    {
        var job = _transfers.Retry(downloadId);

        if (job == null)
        {
            throw new BadRequestException("Only a failed transfer can be tried again, and only while a trusted node still offers the file");
        }

        return TypedResults.Ok(job.DownloadId);
    }

    // Tells open pages to fetch the list again. The list is short, so nothing more is sent.
    [NonAction]
    public void Handle(TransfersChangedEvent message)
    {
        if (!_broadcaster.IsConnected)
        {
            return;
        }

        _broadcaster.BroadcastMessage(new SignalRMessage
        {
            Name = "transfers",
            Body = new ResourceChangeMessage<TransferResource>(ModelAction.Sync),
            Action = ModelAction.Sync,
            Version = 5
        });
    }
}

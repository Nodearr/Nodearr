using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Statistics;

namespace Nodearr.Api.V5.Statistics;

public class StatisticsResource
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string? TimeZone { get; set; }

    // Every day in the range, as yyyy-MM-dd in that time zone.
    public List<string> Days { get; set; } = new();
    public List<DailyBytesResource> Daily { get; set; } = new();
    public List<DirectionTotalsResource> Totals { get; set; } = new();
    public List<FailureCountResource> Failures { get; set; } = new();
    public List<FriendStatisticsResource> Friends { get; set; } = new();
    public UploadQueueResource UploadQueue { get; set; } = new();
    public List<ApplicationStatisticsResource> Applications { get; set; } = new();

    public static StatisticsResource FromModel(StatisticsReport report)
    {
        return new StatisticsResource
        {
            From = report.From,
            To = report.To,
            TimeZone = report.TimeZone,
            Days = report.Days,
            Daily = report.Daily.ConvertAll(d => new DailyBytesResource { Day = d.Day, Direction = d.Direction, PeerNodeId = d.PeerNodeId, ApplicationId = d.ApplicationId, Bytes = d.Bytes }),
            Totals = report.Totals.ConvertAll(t => new DirectionTotalsResource
            {
                Direction = t.Direction,
                Bytes = t.Bytes,
                Finished = t.Finished,
                Failed = t.Failed,
                AverageBytesPerSecond = t.AverageBytesPerSecond,
                PeakBytesPerSecond = t.PeakBytesPerSecond
            }),
            Failures = report.Failures.ConvertAll(f => new FailureCountResource { Direction = f.Direction, Reason = f.Reason, Count = f.Count, Explanation = f.Explanation, GuideUrl = f.GuideUrl }),
            Friends = report.Friends.ConvertAll(f => new FriendStatisticsResource
            {
                PeerNodeId = f.PeerNodeId,
                Name = f.Name,
                Sent = f.Sent,
                Fetched = f.Fetched,
                Finished = f.Finished,
                Failed = f.Failed,
                Checks = f.Checks,
                DirectChecks = f.DirectChecks,
                Route = f.Route,
                RouteChanged = f.RouteChanged
            }),
            UploadQueue = new UploadQueueResource
            {
                Requests = report.UploadQueue.Requests,
                Waited = report.UploadQueue.Waited,
                AverageWaitSeconds = report.UploadQueue.AverageWaitSeconds,
                LongestWaitSeconds = report.UploadQueue.LongestWaitSeconds,
                MaxUploads = report.UploadQueue.MaxUploads,
                LimitMayBeLow = report.UploadQueue.LimitMayBeLow
            },
            Applications = report.Applications.ConvertAll(a => new ApplicationStatisticsResource { ApplicationId = a.ApplicationId, Name = a.Name, SharedTitles = a.SharedTitles, FetchedTitles = a.FetchedTitles })
        };
    }
}

public class DailyBytesResource
{
    public string? Day { get; set; }
    public TransferDirection Direction { get; set; }
    public string? PeerNodeId { get; set; }

    // Zero for files no Arr app could be found for.
    public int ApplicationId { get; set; }
    public long Bytes { get; set; }
}

public class DirectionTotalsResource
{
    public TransferDirection Direction { get; set; }
    public long Bytes { get; set; }
    public int Finished { get; set; }
    public int Failed { get; set; }

    // Bytes moved over the time transfers were moving.
    public double AverageBytesPerSecond { get; set; }

    // The fastest finished transfer's own average.
    public double PeakBytesPerSecond { get; set; }
}

public class FailureCountResource
{
    public TransferDirection Direction { get; set; }

    // A TransferFailure for what was fetched, the transport's word for what was sent.
    public string? Reason { get; set; }
    public int Count { get; set; }
    public string? Explanation { get; set; }
    public string? GuideUrl { get; set; }
}

public class FriendStatisticsResource
{
    public string? PeerNodeId { get; set; }

    // Null for a friend who has since been removed.
    public string? Name { get; set; }
    public long Sent { get; set; }
    public long Fetched { get; set; }
    public int Finished { get; set; }
    public int Failed { get; set; }
    public int Checks { get; set; }
    public int DirectChecks { get; set; }
    public ConnectionPath Route { get; set; }
    public DateTime? RouteChanged { get; set; }
}

public class UploadQueueResource
{
    public int Requests { get; set; }
    public int Waited { get; set; }
    public double AverageWaitSeconds { get; set; }
    public double LongestWaitSeconds { get; set; }

    // Most Uploads at Once on Settings > Node. Zero is no limit.
    public int MaxUploads { get; set; }
    public bool LimitMayBeLow { get; set; }
}

public class ApplicationStatisticsResource
{
    public int ApplicationId { get; set; }
    public string? Name { get; set; }
    public int SharedTitles { get; set; }
    public int FetchedTitles { get; set; }
}

[V5ApiController("statistics")]
public class StatisticsController : Controller
{
    public const int DefaultDays = 30;

    private readonly IStatisticsService _statistics;

    public StatisticsController(IStatisticsService statistics)
    {
        _statistics = statistics;
    }

    // What this node sent and fetched, from whom, how fast and what failed. Without a range, the
    // last 30 days. Days are grouped in the time zone given, an IANA ID such as Europe/London,
    // or in UTC.
    [HttpGet]
    [Produces("application/json")]
    public Ok<StatisticsResource> GetStatistics([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] string? timeZone = null)
    {
        // A time without an offset is taken as UTC.
        var end = to ?? DateTime.UtcNow;
        var start = from ?? end.AddDays(-DefaultDays);

        return TypedResults.Ok(StatisticsResource.FromModel(_statistics.Get(start, end, timeZone ?? string.Empty)));
    }
}

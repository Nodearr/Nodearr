using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Statistics
{
    public interface IStatisticsService
    {
        // What this node did from one moment to another, with days as they are in the time zone
        // given, an IANA or Windows ID. An unknown time zone counts as UTC.
        StatisticsReport Get(DateTime from, DateTime to, string timeZone);
    }

    public class StatisticsReport
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string TimeZone { get; set; }

        // Every day in the range, as yyyy-MM-dd, so a day with nothing still has its place.
        public List<string> Days { get; set; } = new();
        public List<DailyBytes> Daily { get; set; } = new();
        public List<DirectionTotals> Totals { get; set; } = new();
        public List<FailureCount> Failures { get; set; } = new();
        public List<FriendStatistics> Friends { get; set; } = new();
        public UploadQueueStatistics UploadQueue { get; set; } = new();
        public List<ApplicationStatistics> Applications { get; set; } = new();
    }

    public class DailyBytes
    {
        public string Day { get; set; }
        public TransferDirection Direction { get; set; }
        public string PeerNodeId { get; set; }
        public int ApplicationId { get; set; }
        public long Bytes { get; set; }
    }

    public class DirectionTotals
    {
        public TransferDirection Direction { get; set; }
        public long Bytes { get; set; }
        public int Finished { get; set; }
        public int Failed { get; set; }

        // Bytes moved over the time transfers were moving, and the fastest finished transfer's
        // own average. Zero when nothing moved.
        public double AverageBytesPerSecond { get; set; }
        public double PeakBytesPerSecond { get; set; }
    }

    public class FailureCount
    {
        public TransferDirection Direction { get; set; }
        public string Reason { get; set; }
        public int Count { get; set; }
        public string Explanation { get; set; }
        public string GuideUrl { get; set; }
    }

    public class FriendStatistics
    {
        public string PeerNodeId { get; set; }

        // Null for a friend who has since been removed.
        public string Name { get; set; }
        public long Sent { get; set; }
        public long Fetched { get; set; }
        public int Finished { get; set; }
        public int Failed { get; set; }

        // Connection checks in the range, and how many of them found a direct route.
        public int Checks { get; set; }
        public int DirectChecks { get; set; }

        // The last route a check found, and when it last changed, whenever that was.
        public ConnectionPath Route { get; set; }
        public DateTime? RouteChanged { get; set; }
    }

    public class UploadQueueStatistics
    {
        public int Requests { get; set; }
        public int Waited { get; set; }
        public double AverageWaitSeconds { get; set; }
        public double LongestWaitSeconds { get; set; }

        // Most Uploads at Once on Settings > Node. Zero is no limit.
        public int MaxUploads { get; set; }

        // Many requests had to wait, so the limit may be too low.
        public bool LimitMayBeLow { get; set; }
    }

    public class ApplicationStatistics
    {
        // Zero for files no Arr app could be found for.
        public int ApplicationId { get; set; }
        public string Name { get; set; }
        public int SharedTitles { get; set; }
        public int FetchedTitles { get; set; }
    }

    public class StatisticsService : IStatisticsService
    {
        // A limit is called too low once at least this many requests in the range waited, and
        // at least this share of all of them.
        public const int ManyWaited = 5;
        public const double ManyWaitedShare = 0.25;

        private readonly ITransferStatisticRepository _transfers;
        private readonly IConnectionStatisticRepository _connections;
        private readonly IPeerRouteRepository _routes;
        private readonly IFetchedTitleRepository _fetchedTitles;
        private readonly IStatisticsTitles _titles;
        private readonly IPeerRepository _peers;
        private readonly IApplicationFactory _applications;
        private readonly INodeSettingsProvider _settings;

        public StatisticsService(ITransferStatisticRepository transfers,
                                 IConnectionStatisticRepository connections,
                                 IPeerRouteRepository routes,
                                 IFetchedTitleRepository fetchedTitles,
                                 IStatisticsTitles titles,
                                 IPeerRepository peers,
                                 IApplicationFactory applications,
                                 INodeSettingsProvider settings)
        {
            _transfers = transfers;
            _connections = connections;
            _routes = routes;
            _fetchedTitles = fetchedTitles;
            _titles = titles;
            _peers = peers;
            _applications = applications;
            _settings = settings;
        }

        public StatisticsReport Get(DateTime from, DateTime to, string timeZone)
        {
            from = ToUtc(from);
            to = ToUtc(to);

            if (to < from)
            {
                (from, to) = (to, from);
            }

            var zone = FindTimeZone(timeZone);

            // An hour's row counts in full once the range reaches into it.
            var firstHour = StatisticsHours.HourOf(from);
            var rows = _transfers.Between(firstHour, to);
            var checks = _connections.Between(firstHour, to);

            var report = new StatisticsReport { From = from, To = to, TimeZone = zone.Id, Days = DaysBetween(from, to, zone) };

            report.Daily = rows
                .Where(r => r.Bytes != 0)
                .GroupBy(r => (Day: DayOf(r.Hour, zone), r.Direction, r.PeerNodeId, r.ApplicationId))
                .Select(g => new DailyBytes { Day = g.Key.Day, Direction = g.Key.Direction, PeerNodeId = g.Key.PeerNodeId, ApplicationId = g.Key.ApplicationId, Bytes = g.Sum(r => r.Bytes) })
                .OrderBy(d => d.Day, StringComparer.Ordinal)
                .ToList();

            report.Totals = new[] { TransferDirection.Fetched, TransferDirection.Sent }.Select(direction => Totals(direction, rows.Where(r => r.Direction == direction).ToList())).ToList();

            report.Failures = rows
                .Where(r => !r.IsFinished)
                .GroupBy(r => (r.Direction, r.Failure))
                .Select(g => new FailureCount
                {
                    Direction = g.Key.Direction,
                    Reason = g.Key.Failure,
                    Count = g.Sum(r => r.Transfers),
                    Explanation = FailureGuide.Explain(g.Key.Direction, g.Key.Failure),
                    GuideUrl = FailureGuide.GuideUrl(g.Key.Direction, g.Key.Failure)
                })
                .Where(f => f.Count > 0)
                .OrderByDescending(f => f.Count)
                .ThenBy(f => f.Reason, StringComparer.Ordinal)
                .ToList();

            report.Friends = Friends(rows, checks);
            report.UploadQueue = UploadQueue(rows.Where(r => r.Direction == TransferDirection.Sent).ToList());
            report.Applications = Applications(rows, from, to);

            return report;
        }

        private static DirectionTotals Totals(TransferDirection direction, List<TransferStatistic> rows)
        {
            var bytes = rows.Sum(r => r.Bytes);
            var seconds = rows.Sum(r => r.Seconds);

            return new DirectionTotals
            {
                Direction = direction,
                Bytes = bytes,
                Finished = rows.Where(r => r.IsFinished).Sum(r => r.Transfers),
                Failed = rows.Where(r => !r.IsFinished).Sum(r => r.Transfers),
                AverageBytesPerSecond = seconds > 0 ? bytes / seconds : 0,
                PeakBytesPerSecond = rows.Select(r => r.PeakBytesPerSecond).DefaultIfEmpty(0).Max()
            };
        }

        // Every trusted friend, and anyone else the range has something for.
        private List<FriendStatistics> Friends(List<TransferStatistic> rows, List<ConnectionStatistic> checks)
        {
            var peers = _peers.All();
            var names = peers.GroupBy(p => p.NodeId).ToDictionary(g => g.Key, g => g.First().Name);
            var routes = _routes.All().GroupBy(r => r.PeerNodeId).ToDictionary(g => g.Key, g => g.First());

            var nodeIds = peers.Where(p => p.State == PeerState.Trusted).Select(p => p.NodeId)
                .Concat(rows.Select(r => r.PeerNodeId))
                .Concat(checks.Select(c => c.PeerNodeId))
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct();

            return nodeIds.Select(nodeId =>
                {
                    var theirs = rows.Where(r => r.PeerNodeId == nodeId).ToList();
                    var theirChecks = checks.Where(c => c.PeerNodeId == nodeId).ToList();
                    var route = routes.GetValueOrDefault(nodeId);

                    return new FriendStatistics
                    {
                        PeerNodeId = nodeId,
                        Name = names.GetValueOrDefault(nodeId),
                        Sent = theirs.Where(r => r.Direction == TransferDirection.Sent).Sum(r => r.Bytes),
                        Fetched = theirs.Where(r => r.Direction == TransferDirection.Fetched).Sum(r => r.Bytes),
                        Finished = theirs.Where(r => r.IsFinished).Sum(r => r.Transfers),
                        Failed = theirs.Where(r => !r.IsFinished).Sum(r => r.Transfers),
                        Checks = theirChecks.Sum(c => c.Direct + c.RelayOnly + c.None),
                        DirectChecks = theirChecks.Sum(c => c.Direct),
                        Route = route?.Path ?? ConnectionPath.Unknown,
                        RouteChanged = route?.Changed
                    };
                })
                .OrderBy(f => f.Name ?? "￿", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(f => f.PeerNodeId, StringComparer.Ordinal)
                .ToList();
        }

        private UploadQueueStatistics UploadQueue(List<TransferStatistic> sent)
        {
            var requests = sent.Sum(r => r.Requests);
            var waited = sent.Sum(r => r.Waited);

            return new UploadQueueStatistics
            {
                Requests = requests,
                Waited = waited,
                AverageWaitSeconds = waited > 0 ? sent.Sum(r => r.WaitSeconds) / waited : 0,
                LongestWaitSeconds = sent.Select(r => r.LongestWaitSeconds).DefaultIfEmpty(0).Max(),
                MaxUploads = _settings.Get().MaxUploads,
                LimitMayBeLow = waited >= ManyWaited && waited >= requests * ManyWaitedShare
            };
        }

        // Each Arr app there is now, and any other the range has something for.
        private List<ApplicationStatistics> Applications(List<TransferStatistic> rows, DateTime from, DateTime to)
        {
            var applications = _applications.All().ToDictionary(a => a.Id, a => a.Name);
            var shared = _titles.SharedTitles();
            var fetched = _fetchedTitles.CountDistinct(StatisticsHours.HourOf(from), to);

            return applications.Keys
                .Concat(rows.Select(r => r.ApplicationId))
                .Concat(fetched.Keys)
                .Distinct()
                .Select(id => new ApplicationStatistics
                {
                    ApplicationId = id,
                    Name = applications.GetValueOrDefault(id),
                    SharedTitles = shared.GetValueOrDefault(id),
                    FetchedTitles = fetched.GetValueOrDefault(id)
                })
                .OrderBy(a => a.ApplicationId == 0)
                .ThenBy(a => a.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static List<string> DaysBetween(DateTime from, DateTime to, TimeZoneInfo zone)
        {
            var first = TimeZoneInfo.ConvertTimeFromUtc(from, zone).Date;
            var last = TimeZoneInfo.ConvertTimeFromUtc(to > from ? to.AddTicks(-1) : from, zone).Date;
            var days = new List<string>();

            for (var day = first; day <= last; day = day.AddDays(1))
            {
                days.Add(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            return days;
        }

        private static string DayOf(DateTime hour, TimeZoneInfo zone)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(hour, DateTimeKind.Utc), zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static DateTime ToUtc(DateTime time)
        {
            return time.Kind switch
            {
                DateTimeKind.Local => time.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
                _ => time
            };
        }

        private static TimeZoneInfo FindTimeZone(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return TimeZoneInfo.Utc;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}

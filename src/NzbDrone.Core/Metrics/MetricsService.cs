using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Metrics
{
    public interface IMetricsService
    {
        // What /metrics gives, read when it is asked for. Nothing is kept or sent.
        List<MetricFamily> Collect();
    }

    // This node's numbers for Prometheus. The totals are the statistics' hourly rows added up
    // since the start (Statistics), which only go up. The rest is read from live state.
    //
    // A friend is labelled by display name, never by node ID, and an Arr app by its name in
    // Nodearr. A friend or app removed since has no name to give, so its totals are left out.
    // Files no Arr app could be found for have no app label.
    public class MetricsService : IMetricsService
    {
        private readonly ITransferStatisticRepository _transferStatistics;
        private readonly IConnectionStatisticRepository _connectionStatistics;
        private readonly IPeerRepository _peers;
        private readonly IApplicationFactory _applications;
        private readonly ITransferService _transfers;
        private readonly IUploadTracker _uploads;
        private readonly ICatalogService _catalog;
        private readonly IHealthCheckService _health;
        private readonly IConfigFileProvider _config;

        public MetricsService(ITransferStatisticRepository transferStatistics,
                              IConnectionStatisticRepository connectionStatistics,
                              IPeerRepository peers,
                              IApplicationFactory applications,
                              ITransferService transfers,
                              IUploadTracker uploads,
                              ICatalogService catalog,
                              IHealthCheckService health,
                              IConfigFileProvider config)
        {
            _transferStatistics = transferStatistics;
            _connectionStatistics = connectionStatistics;
            _peers = peers;
            _applications = applications;
            _transfers = transfers;
            _uploads = uploads;
            _catalog = catalog;
            _health = health;
            _config = config;
        }

        public List<MetricFamily> Collect()
        {
            var peers = _peers.All().ToList();
            var friends = peers.Where(p => !string.IsNullOrEmpty(p.NodeId) && !string.IsNullOrEmpty(p.Name))
                .GroupBy(p => p.NodeId)
                .ToDictionary(g => g.Key, g => g.First().Name);
            var apps = _applications.All().ToDictionary(a => a.Id, a => a.Name);

            var families = new List<MetricFamily>();

            families.AddRange(Totals(friends, apps));
            families.AddRange(Live(peers, friends));

            return families;
        }

        private IEnumerable<MetricFamily> Totals(Dictionary<string, string> friends, Dictionary<int, string> apps)
        {
            var bytes = new MetricFamily("nodearr_transfer_bytes", MetricType.Counter, "Bytes sent to friends and fetched from them since statistics began, attempts that failed included.");
            var transfers = new MetricFamily("nodearr_transfers", MetricType.Counter, "Transfers that ended, finished or failed, since statistics began. A failed upload the friend asks for again still counts here.");
            var checks = new MetricFamily("nodearr_connection_checks", MetricType.Counter, "Connection checks to each friend since statistics began, by the route found.");
            var waitSeconds = new MetricFamily("nodearr_upload_queue_wait_seconds", MetricType.Counter, "Seconds requests from friends waited for a free upload slot, in all.");
            var waits = new MetricFamily("nodearr_upload_queue_waits", MetricType.Counter, "Requests from friends that had to wait for a free upload slot.");

            foreach (var row in _transferStatistics.Totals())
            {
                // Not by friend, so a friend removed still counts, and the totals stay whole.
                if (row.Direction == TransferDirection.Sent)
                {
                    waitSeconds.Add(row.WaitSeconds);
                    waits.Add(row.Waited);
                }

                if (!friends.TryGetValue(row.PeerNodeId ?? string.Empty, out var friend))
                {
                    continue;
                }

                string app = null;

                if (row.ApplicationId != 0 && !apps.TryGetValue(row.ApplicationId, out app))
                {
                    continue;
                }

                var direction = Direction(row.Direction);

                bytes.Add(row.Bytes, ("direction", direction), ("friend", friend), ("app", app));

                if (row.IsFinished)
                {
                    transfers.Add(row.Transfers, ("direction", direction), ("friend", friend), ("result", "finished"));
                }
                else
                {
                    transfers.Add(row.Transfers, ("direction", direction), ("friend", friend), ("result", "failed"), ("reason", row.Failure));
                }
            }

            foreach (var row in _connectionStatistics.Totals())
            {
                if (!friends.TryGetValue(row.PeerNodeId ?? string.Empty, out var friend))
                {
                    continue;
                }

                checks.Add(row.Direct, ("friend", friend), ("route", "direct"));
                checks.Add(row.RelayOnly, ("friend", friend), ("route", "relay"));
                checks.Add(row.None, ("friend", friend), ("route", "none"));
            }

            // There always is one of each, even before anything waited.
            waitSeconds.Add(0);
            waits.Add(0);

            return new[] { bytes, transfers, checks, waitSeconds, waits };
        }

        private IEnumerable<MetricFamily> Live(List<Peer> peers, Dictionary<string, string> friends)
        {
            var jobs = _transfers.All();
            var uploads = _uploads.All();

            var active = new MetricFamily("nodearr_transfers_active", MetricType.Gauge, "Transfers moving data now.")
                .Add(jobs.Count(j => j.State == TransferState.Downloading), ("direction", "fetched"))
                .Add(uploads.Count(u => u.State is UploadState.Preparing or UploadState.Sending), ("direction", "sent"));

            var uploadQueue = new MetricFamily("nodearr_upload_queue_length", MetricType.Gauge, "Requests from friends waiting for a free upload slot now.")
                .Add(uploads.Count(u => u.State == UploadState.Waiting));

            var downloadQueue = new MetricFamily("nodearr_download_queue_length", MetricType.Gauge, "Transfers from friends waiting to start, or to try again, now.")
                .Add(jobs.Count(j => j.State == TransferState.Queued));

            var route = new MetricFamily("nodearr_peer_route", MetricType.Gauge, "1 for the route the last connection check found to each trusted friend, 0 for the others.", sumAlike: false);
            var lastSeen = new MetricFamily("nodearr_peer_last_seen_timestamp_seconds", MetricType.Gauge, "When each trusted friend was last heard from, in seconds since 1970 UTC.", sumAlike: false);

            foreach (var peer in peers.Where(p => p.State == PeerState.Trusted))
            {
                if (!friends.TryGetValue(peer.NodeId ?? string.Empty, out var friend))
                {
                    continue;
                }

                var found = Route(peer.ConnectionPath);

                foreach (var name in new[] { "direct", "relay", "none" })
                {
                    route.Add(name == found ? 1 : 0, ("friend", friend), ("route", name));
                }

                if (peer.LastSeen.HasValue)
                {
                    lastSeen.Add(UnixSeconds(peer.LastSeen.Value), ("friend", friend));
                }
            }

            var titles = new MetricFamily("nodearr_catalog_titles", MetricType.Gauge, "Series, movies and albums in each Arr app's library, as the catalog holds them.");

            foreach (var summary in _catalog.GetSummaries())
            {
                titles.Add(summary.Series + summary.Movies + summary.Albums, ("app", summary.ApplicationName));
            }

            var health = new MetricFamily("nodearr_health_issues", MetricType.Gauge, "Issues System > Status shows now, by type.");
            var issues = _health.Results().Where(h => h.Type != HealthCheckResult.Ok).ToList();

            foreach (var type in new[] { HealthCheckResult.Notice, HealthCheckResult.Warning, HealthCheckResult.Error })
            {
                health.Add(issues.Count(h => h.Type == type), ("type", type.ToString().ToLowerInvariant()));
            }

            var info = new MetricFamily("nodearr", MetricType.Info, "The version of Nodearr running, and its update branch.")
                .Add(1, ("version", BuildInfo.Version.ToString()), ("branch", _config.Branch));

            return new[] { active, uploadQueue, downloadQueue, route, lastSeen, titles, health, info };
        }

        private static string Direction(TransferDirection direction)
        {
            return direction == TransferDirection.Sent ? "sent" : "fetched";
        }

        private static string Route(ConnectionPath path)
        {
            return path switch
            {
                ConnectionPath.Direct => "direct",
                ConnectionPath.Relay => "relay",
                _ => "none"
            };
        }

        private static double UnixSeconds(DateTime time)
        {
            var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);

            return Math.Floor((utc - DateTime.UnixEpoch).TotalSeconds);
        }
    }
}

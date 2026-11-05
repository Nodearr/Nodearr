using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Metrics;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;
using Health = NzbDrone.Core.HealthCheck.HealthCheck;

namespace NzbDrone.Core.Test.Metrics
{
    [TestFixture]
    public class MetricsServiceFixture : DbTest<MetricsService, TransferStatistic>
    {
        private const string AliceNodeId = "nodeid-alice-000000000000000000000000000000000000000000";
        private const string BobNodeId = "nodeid-bob-00000000000000000000000000000000000000000000";

        private static readonly DateTime Now = StatisticsHours.HourOf(DateTime.UtcNow);

        private List<Peer> _peers;
        private List<TransferJob> _jobs;
        private List<Upload> _uploads;
        private List<Health> _health;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ITransferStatisticRepository>(Mocker.Resolve<TransferStatisticRepository>());
            Mocker.SetConstant<IConnectionStatisticRepository>(Mocker.Resolve<ConnectionStatisticRepository>());

            _peers = new List<Peer>
            {
                new() { NodeId = AliceNodeId, Name = "Alice", State = PeerState.Trusted, ConnectionPath = ConnectionPath.Direct, LastSeen = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc) },
                new() { NodeId = BobNodeId, Name = "Bob", State = PeerState.Trusted, ConnectionPath = ConnectionPath.Relay }
            };
            _jobs = new List<TransferJob>();
            _uploads = new List<Upload>();
            _health = new List<Health>();

            Mocker.GetMock<IPeerRepository>().Setup(r => r.All()).Returns(() => _peers);
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(new List<ApplicationDefinition>
            {
                new() { Id = 3, Name = "Sonarr" },
                new() { Id = 4, Name = "Radarr" }
            });
            Mocker.GetMock<ITransferService>().Setup(s => s.All()).Returns(() => _jobs);
            Mocker.GetMock<IUploadTracker>().Setup(s => s.All()).Returns(() => _uploads);
            Mocker.GetMock<ICatalogService>().Setup(s => s.GetSummaries()).Returns(new List<CatalogApplicationSummary>
            {
                new() { ApplicationId = 3, ApplicationName = "Sonarr", Series = 12 },
                new() { ApplicationId = 4, ApplicationName = "Radarr", Movies = 40 }
            });
            Mocker.GetMock<IHealthCheckService>().Setup(s => s.Results()).Returns(() => _health);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.Branch).Returns("main");
        }

        private void GivenRow(DateTime hour, TransferDirection direction, long bytes, string failure = "", int transfers = 1, string peer = AliceNodeId, int applicationId = 3, int waited = 0, double waitSeconds = 0, int reopened = 0)
        {
            Db.Insert(new TransferStatistic
            {
                Hour = hour,
                PeerNodeId = peer,
                ApplicationId = applicationId,
                Direction = direction,
                Failure = failure,
                Bytes = bytes,
                Transfers = transfers,
                Reopened = reopened,
                Seconds = 1,
                Waited = waited,
                WaitSeconds = waitSeconds
            });
        }

        private ExpositionParser.Result Scrape(bool openMetrics = false)
        {
            return ExpositionParser.Parse(PrometheusText.Write(Subject.Collect(), openMetrics), openMetrics);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void the_output_should_parse_with_everything_in_it(bool openMetrics)
        {
            GivenRow(Now, TransferDirection.Sent, 100);
            GivenRow(Now, TransferDirection.Fetched, 100, "NotFound");
            Db.Insert(new ConnectionStatistic { Hour = Now, PeerNodeId = AliceNodeId, Direct = 1 });
            _health.Add(new Health(typeof(MetricsServiceFixture), HealthCheckResult.Warning, HealthCheckReason.PeerRelayOnly, "Relay only"));

            var parsed = Scrape(openMetrics);

            parsed.Samples.Select(s => s.Name).Distinct().Should().BeEquivalentTo(
                "nodearr_transfer_bytes_total",
                "nodearr_transfers_total",
                "nodearr_connection_checks_total",
                "nodearr_upload_queue_wait_seconds_total",
                "nodearr_upload_queue_waits_total",
                "nodearr_transfers_active",
                "nodearr_upload_queue_length",
                "nodearr_download_queue_length",
                "nodearr_peer_route",
                "nodearr_peer_last_seen_timestamp_seconds",
                "nodearr_catalog_titles",
                "nodearr_health_issues",
                "nodearr_info");
        }

        // The totals are every hour since the start: within the 13 months, and what was carried
        // from before them.
        [Test]
        public void the_totals_should_match_the_rollup()
        {
            GivenRow(Now.AddMonths(-15), TransferDirection.Sent, 1000, waited: 1, waitSeconds: 5);
            GivenRow(Now.AddDays(-3), TransferDirection.Sent, 2000, waited: 2, waitSeconds: 7.5);
            GivenRow(Now, TransferDirection.Sent, 500, applicationId: 4);
            GivenRow(Now, TransferDirection.Fetched, 300, "ConnectionLost", transfers: 2, peer: BobNodeId);
            GivenRow(Now.AddMonths(-14), TransferDirection.Sent, 50, "DirectPathLost", transfers: 3, reopened: 2);
            GivenRow(Now, TransferDirection.Sent, 0, "DirectPathLost", transfers: 1);
            Db.Insert(new ConnectionStatistic { Hour = Now.AddMonths(-15), PeerNodeId = AliceNodeId, Direct = 4, RelayOnly = 1 });
            Db.Insert(new ConnectionStatistic { Hour = Now, PeerNodeId = AliceNodeId, Direct = 2, None = 3 });

            Mocker.Resolve<TrimStatistics>().Clean();

            var parsed = Scrape();

            parsed.Value("nodearr_transfer_bytes_total", ("direction", "sent"), ("friend", "Alice"), ("app", "Sonarr")).Should().Be(3050);
            parsed.Value("nodearr_transfer_bytes_total", ("direction", "sent"), ("friend", "Alice"), ("app", "Radarr")).Should().Be(500);
            parsed.Value("nodearr_transfer_bytes_total", ("direction", "fetched"), ("friend", "Bob"), ("app", "Sonarr")).Should().Be(300);
            parsed.Named("nodearr_transfer_bytes_total").Sum(s => s.Value).Should().Be(AllStoredModels.Sum(s => s.Bytes));

            parsed.Value("nodearr_transfers_total", ("direction", "sent"), ("friend", "Alice"), ("result", "finished")).Should().Be(3);

            // The taken-back failures still count: the total only goes up.
            parsed.Value("nodearr_transfers_total", ("direction", "sent"), ("friend", "Alice"), ("result", "failed"), ("reason", "DirectPathLost")).Should().Be(4);
            parsed.Value("nodearr_transfers_total", ("direction", "fetched"), ("friend", "Bob"), ("result", "failed"), ("reason", "ConnectionLost")).Should().Be(2);

            parsed.Value("nodearr_connection_checks_total", ("friend", "Alice"), ("route", "direct")).Should().Be(6);
            parsed.Value("nodearr_connection_checks_total", ("friend", "Alice"), ("route", "relay")).Should().Be(1);
            parsed.Value("nodearr_connection_checks_total", ("friend", "Alice"), ("route", "none")).Should().Be(3);

            parsed.Value("nodearr_upload_queue_waits_total").Should().Be(3);
            parsed.Value("nodearr_upload_queue_wait_seconds_total").Should().Be(12.5);
        }

        [Test]
        public void no_node_id_should_appear()
        {
            GivenRow(Now, TransferDirection.Sent, 100);
            GivenRow(Now, TransferDirection.Fetched, 100, "NotFound", peer: BobNodeId);
            Db.Insert(new ConnectionStatistic { Hour = Now, PeerNodeId = BobNodeId, RelayOnly = 1 });

            var text = PrometheusText.Write(Subject.Collect(), false);

            text.Should().Contain("friend=\"Alice\"").And.Contain("friend=\"Bob\"");
            text.Should().NotContain(AliceNodeId).And.NotContain(BobNodeId);
            text.Should().NotContain("nodeid-");
        }

        [Test]
        public void a_removed_friend_or_app_should_be_left_out()
        {
            GivenRow(Now, TransferDirection.Sent, 100, peer: "nodeid-removed", waited: 1, waitSeconds: 2);
            GivenRow(Now, TransferDirection.Sent, 200, applicationId: 9);
            GivenRow(Now, TransferDirection.Sent, 300);

            var parsed = Scrape();

            parsed.Named("nodearr_transfer_bytes_total").Should().ContainSingle().Which.Value.Should().Be(300);
            parsed.Value("nodearr_upload_queue_waits_total").Should().Be(1);
        }

        [Test]
        public void a_file_with_no_app_should_have_no_app_label()
        {
            GivenRow(Now, TransferDirection.Sent, 100, applicationId: 0);

            Scrape().Value("nodearr_transfer_bytes_total", ("direction", "sent"), ("friend", "Alice")).Should().Be(100);
        }

        [Test]
        public void friends_with_the_same_name_should_be_one_series()
        {
            _peers[1].Name = "Alice";
            GivenRow(Now, TransferDirection.Sent, 100);
            GivenRow(Now, TransferDirection.Sent, 200, peer: BobNodeId);

            var parsed = Scrape();

            parsed.Value("nodearr_transfer_bytes_total", ("direction", "sent"), ("friend", "Alice"), ("app", "Sonarr")).Should().Be(300);
            parsed.Value("nodearr_peer_route", ("friend", "Alice"), ("route", "direct")).Should().Be(1);
            parsed.Value("nodearr_peer_route", ("friend", "Alice"), ("route", "relay")).Should().Be(1);
        }

        [Test]
        public void live_state_should_be_read_now()
        {
            _jobs.Add(new TransferJob { State = TransferState.Downloading });
            _jobs.Add(new TransferJob { State = TransferState.Queued });
            _jobs.Add(new TransferJob { State = TransferState.Queued });
            _jobs.Add(new TransferJob { State = TransferState.Completed });
            _uploads.Add(new Upload { State = UploadState.Sending });
            _uploads.Add(new Upload { State = UploadState.Preparing });
            _uploads.Add(new Upload { State = UploadState.Waiting });
            _uploads.Add(new Upload { State = UploadState.Failed });
            _peers.Add(new Peer { NodeId = "nodeid-carol", Name = "Carol", State = PeerState.Revoked, ConnectionPath = ConnectionPath.Direct });
            _health.Add(new Health(typeof(MetricsServiceFixture), HealthCheckResult.Warning, HealthCheckReason.PeerRelayOnly, "Relay only"));
            _health.Add(new Health(typeof(PrometheusTextFixture), HealthCheckResult.Error, HealthCheckReason.PeerTransportNotRunning, "Not running"));

            var parsed = Scrape();

            parsed.Value("nodearr_transfers_active", ("direction", "fetched")).Should().Be(1);
            parsed.Value("nodearr_transfers_active", ("direction", "sent")).Should().Be(2);
            parsed.Value("nodearr_download_queue_length").Should().Be(2);
            parsed.Value("nodearr_upload_queue_length").Should().Be(1);

            parsed.Value("nodearr_peer_route", ("friend", "Alice"), ("route", "direct")).Should().Be(1);
            parsed.Value("nodearr_peer_route", ("friend", "Alice"), ("route", "relay")).Should().Be(0);
            parsed.Value("nodearr_peer_route", ("friend", "Bob"), ("route", "relay")).Should().Be(1);
            parsed.Named("nodearr_peer_route").Should().NotContain(s => s.Labels["friend"] == "Carol");

            parsed.Value("nodearr_peer_last_seen_timestamp_seconds", ("friend", "Alice")).Should().Be(1793793600);
            parsed.Named("nodearr_peer_last_seen_timestamp_seconds").Should().ContainSingle();

            parsed.Value("nodearr_catalog_titles", ("app", "Sonarr")).Should().Be(12);
            parsed.Value("nodearr_catalog_titles", ("app", "Radarr")).Should().Be(40);

            parsed.Value("nodearr_health_issues", ("type", "notice")).Should().Be(0);
            parsed.Value("nodearr_health_issues", ("type", "warning")).Should().Be(1);
            parsed.Value("nodearr_health_issues", ("type", "error")).Should().Be(1);

            parsed.Named("nodearr_info").Should().ContainSingle().Which.Labels.Should().ContainKey("version").And.Contain("branch", "main");
        }

        [Test]
        public void before_anything_happened_the_output_should_still_parse()
        {
            _peers.Clear();

            var parsed = Scrape(true);

            parsed.Value("nodearr_upload_queue_waits_total").Should().Be(0);
            parsed.Named("nodearr_transfer_bytes_total").Should().BeEmpty();
        }
    }
}

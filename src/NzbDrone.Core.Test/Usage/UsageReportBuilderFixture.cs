using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Radarr;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;
using NzbDrone.Core.Update;
using NzbDrone.Core.Usage;

namespace NzbDrone.Core.Test.Usage
{
    [TestFixture]
    public class UsageReportBuilderFixture : DbTest<UsageReportBuilder, TransferStatistic>
    {
        // What this node knows that must never leave it.
        private const string FriendName = "Alice's media server";
        private const string FriendNodeId = "5c0f6c7a9b8e4d3c2b1a0f9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a2f1e0d9c";
        private const string OtherNodeId = "9d8c7b6a5f4e3d2c1b0a9f8e7d6c5b4a3f2e1d0c9b8a7f6e5d4c3b2a1f0e9d8c";
        private const string SonarrUrl = "http://sonarr.example.lan:8989";
        private const string RadarrUrl = "http://192.168.50.17:7878";
        private const string ApiKey = "0123456789abcdef0123456789abcdef";
        private const string PublicAddress = "203.0.113.45:51820";
        private const string RelayUrl = "https://relay.example.net/";
        private const string Title = "Night of the Living Dead";
        private const string DownloadFolder = "/srv/media/downloads";

        private static readonly DateTime Now = new(2026, 11, 6, 15, 30, 0, DateTimeKind.Utc);

        private List<Peer> _peers;
        private List<ApplicationDefinition> _applications;
        private NodeSettings _settings;
        private NetworkStatus _network;
        private List<TransferJob> _activeTransfers;
        private List<SentTitle> _sent;
        private List<ShareListDetails> _lists;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<ITransferStatisticRepository>(Mocker.Resolve<TransferStatisticRepository>());
            Mocker.SetConstant<IConnectionStatisticRepository>(Mocker.Resolve<ConnectionStatisticRepository>());
            Mocker.SetConstant<IConfigRepository>(Mocker.Resolve<ConfigRepository>());

            _peers = new List<Peer>
            {
                new() { Id = 1, NodeId = FriendNodeId, Name = FriendName, State = PeerState.Trusted, ShareAll = true, IpAddresses = "198.51.100.23:4433", RelayUrl = RelayUrl },
                new() { Id = 2, NodeId = OtherNodeId, Name = "Bob", State = PeerState.Trusted },
                new() { Id = 3, NodeId = "blocked", Name = "Mallory", State = PeerState.Blocked }
            };

            _applications = new List<ApplicationDefinition>
            {
                new() { Id = 3, Name = "Living room Sonarr", Implementation = "Sonarr", SyncLevel = ApplicationSyncLevel.FullSync, Settings = new SonarrSettings { BaseUrl = SonarrUrl, ApiKey = ApiKey } },
                new() { Id = 4, Name = "Radarr", Implementation = "Radarr", SyncLevel = ApplicationSyncLevel.FullSync, Settings = new RadarrSettings { BaseUrl = RadarrUrl, ApiKey = ApiKey, FetchNewFilesAutomatically = false } }
            };

            _settings = new NodeSettings { NodeName = "home-server", Relay = RelayMode.Custom, RelayUrl = RelayUrl, MaxUploads = NodeSettingsDefaults.MaxUploads, DownloadFolder = DownloadFolder, UploadLimitKbps = 500 };
            _network = new NetworkStatus { Udp = true, MappingVaries = false, PublicAddress = PublicAddress, RelayUrl = RelayUrl, IpAddresses = new List<string> { "192.168.50.2:4433" } };
            _activeTransfers = new List<TransferJob>();
            _sent = new List<SentTitle>();
            _lists = new List<ShareListDetails>
            {
                new() { List = new ShareList { Id = 1, Name = "Hidden", IsHidden = true } }
            };

            Mocker.GetMock<IPeerRepository>().Setup(r => r.All()).Returns(() => _peers);
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(() => _applications);
            Mocker.GetMock<IArrV3Proxy>().Setup(p => p.GetVersion(It.Is<IArrConnection>(c => c.BaseUrl == SonarrUrl))).Returns(new Version(4, 0, 17, 2952));
            Mocker.GetMock<IArrV3Proxy>().Setup(p => p.GetVersion(It.Is<IArrConnection>(c => c.BaseUrl == RadarrUrl))).Throws(new Exception("Connection refused (192.168.50.17:7878)"));
            Mocker.GetMock<IStatisticsTitles>().Setup(t => t.SharedTitles()).Returns(new Dictionary<int, int> { [3] = 12, [4] = 0 });
            Mocker.GetMock<INodeSettingsProvider>().Setup(s => s.Get()).Returns(() => _settings);
            Mocker.GetMock<IPeerTransport>().Setup(t => t.GetNetworkStatus()).Returns(() => _network);
            Mocker.GetMock<ITransferRepository>().Setup(r => r.Active()).Returns(() => _activeTransfers);
            Mocker.GetMock<ISentTitleRepository>().Setup(r => r.All()).Returns(() => _sent);
            Mocker.GetMock<ISharingService>().Setup(s => s.All()).Returns(() => _lists);
            Mocker.GetMock<ILocalizationService>().Setup(l => l.GetLanguageIdentifier()).Returns("en");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.Branch).Returns("main");
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.StartUpFolder).Returns(OsInfo.IsWindows ? @"C:\Nodearr\bin" : "/opt/nodearr");
        }

        private void GivenTransfers(DateTime hour, TransferDirection direction, int transfers, long bytes, string failure = "", int reopened = 0)
        {
            Db.Insert(new TransferStatistic
            {
                Hour = StatisticsHours.HourOf(hour),
                PeerNodeId = FriendNodeId,
                ApplicationId = 3,
                Direction = direction,
                Failure = failure,
                Bytes = bytes,
                Transfers = transfers,
                Reopened = reopened,
                Seconds = 10
            });
        }

        private void GivenChecks(string peerNodeId, DateTime hour, int direct = 0, int relayOnly = 0, int none = 0)
        {
            Db.Insert(new ConnectionStatistic { Hour = StatisticsHours.HourOf(hour), PeerNodeId = peerNodeId, Direct = direct, RelayOnly = relayOnly, None = none });
        }

        private UsageReport Build(DateTime? now = null)
        {
            return Subject.Build(now ?? Now);
        }

        private static string Compact(string json)
        {
            return JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
        }

        private string BuildJson(DateTime? now = null)
        {
            return UsageReportBuilder.ToJson(Build(now));
        }

        private void GivenABusyNode()
        {
            GivenTransfers(Now.AddDays(-1), TransferDirection.Sent, 7, 3 * UsageBuckets.GB);
            GivenTransfers(Now.AddDays(-2), TransferDirection.Sent, 2, 0, "NoDirectRoute");
            GivenTransfers(Now.AddDays(-2), TransferDirection.Fetched, 1, 200_000_000, "DiskError");
            GivenTransfers(Now.AddDays(-3), TransferDirection.Fetched, 30, 120 * UsageBuckets.GB);
            GivenChecks(FriendNodeId, Now.AddHours(-3), direct: 2, relayOnly: 1);
            GivenChecks(OtherNodeId, Now.AddHours(-5), relayOnly: 4);
            _sent.Add(new SentTitle { Title = Title, Summary = Title, Created = Now.AddDays(-1) });
            _lists.Add(new ShareListDetails { List = new ShareList { Id = 2, Name = "Films for Alice" }, Items = new List<ShareKey> { ShareKey.Movie(4, 1) }, PeerIds = new List<int> { 1 } });
        }

        [Test]
        public void a_report_should_hold_only_what_the_schema_allows()
        {
            GivenABusyNode();

            ReportSchema.Errors(BuildJson()).Should().BeEmpty();
        }

        // The example the privacy page and usage.nodearr.com's tests use.
        [Test]
        public void the_published_example_report_should_be_one_the_schema_allows_and_this_node_could_send()
        {
            var json = System.IO.File.ReadAllText(System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Usage", "example-report.json"));

            ReportSchema.Errors(json).Should().BeEmpty();

            var example = JsonSerializer.Deserialize<UsageReport>(json, UsageReportBuilder.JsonOptions);
            Compact(UsageReportBuilder.ToJson(example)).Should().Be(Compact(json));
        }

        [Test]
        public void a_report_from_a_new_node_should_hold_only_what_the_schema_allows()
        {
            _peers.Clear();
            _applications.Clear();
            _network = new NetworkStatus();

            ReportSchema.Errors(BuildJson()).Should().BeEmpty();
        }

        [Test]
        public void a_report_should_hold_nothing_that_identifies_the_node_its_owner_or_a_friend()
        {
            GivenABusyNode();

            var json = BuildJson();

            foreach (var secret in new[] { FriendName, "Bob", "Mallory", FriendNodeId, OtherNodeId, "Living room", SonarrUrl, "sonarr.example.lan", RadarrUrl, "192.168.50.17", ApiKey, PublicAddress, "203.0.113.45", "198.51.100.23", "192.168.50.2", RelayUrl, "relay.example.net", Title, "Films for Alice", DownloadFolder, "home-server" })
            {
                json.Should().NotContain(secret);
            }
        }

        [Test]
        public void a_schema_that_had_a_field_the_report_lacks_or_the_other_way_round_should_be_noticed()
        {
            var report = JsonDocument.Parse(BuildJson()).RootElement;
            var withExtra = JsonSerializer.Serialize(report.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value).Append(new KeyValuePair<string, object>("hostname", "home-server")).ToDictionary(p => p.Key, p => p.Value));
            var withoutLanguage = JsonSerializer.Serialize(report.EnumerateObject().Where(p => p.Name != "language").ToDictionary(p => p.Name, p => (object)p.Value));

            ReportSchema.Errors(withExtra).Should().Contain(e => e.Contains("hostname"));
            ReportSchema.Errors(withoutLanguage).Should().Contain(e => e.Contains("language"));
        }

        [Test]
        public void counts_should_be_ranges_never_exact()
        {
            GivenABusyNode();

            var report = Build();

            report.Friends.Should().Be("2-5");
            report.Past7Days.Sent.Finished.Should().Be("6-25");
            report.Past7Days.Sent.Failed.Should().Be("1-5");
            report.Past7Days.Sent.Data.Should().Be("1-10GB");
            report.Past7Days.Sent.Failures.Should().Equal(new Dictionary<string, string> { ["NoDirectRoute"] = "1-5" });
            report.Past7Days.Fetched.Finished.Should().Be("26-100");
            report.Past7Days.Fetched.Data.Should().Be("100GB-1TB");
            report.Past7Days.Fetched.Failures.Should().Equal(new Dictionary<string, string> { ["DiskError"] = "1-5" });
            report.Past7Days.Sends.Should().Be("1-5");
            report.SharedTitles.Should().Equal(new Dictionary<string, string> { ["radarr"] = "0", ["sonarr"] = "11-100" });
        }

        [Test]
        public void only_the_past_7_days_should_be_counted()
        {
            GivenTransfers(Now.AddDays(-8), TransferDirection.Sent, 40, 5 * UsageBuckets.GB);
            GivenChecks(FriendNodeId, Now.AddDays(-8), direct: 1);

            var report = Build();

            report.Past7Days.Sent.Finished.Should().Be("0");
            report.Past7Days.Sent.Data.Should().Be("0");
            report.Network.FriendsDirect.Should().Be("0");
        }

        [Test]
        public void a_failure_asked_for_again_afterwards_should_not_count_as_failed()
        {
            GivenTransfers(Now.AddDays(-1), TransferDirection.Sent, 2, 0, "Stopped", reopened: 2);

            var report = Build();

            report.Past7Days.Sent.Failed.Should().Be("0");
            report.Past7Days.Sent.Failures.Should().BeEmpty();
        }

        [Test]
        public void a_failure_reason_the_report_doesnt_know_should_be_other()
        {
            GivenTransfers(Now.AddDays(-1), TransferDirection.Sent, 1, 0, "SomethingNew at C:\\Users\\owner");

            Build().Past7Days.Sent.Failures.Should().Equal(new Dictionary<string, string> { ["Other"] = "1-5" });
        }

        [Test]
        public void friends_should_be_counted_by_the_routes_found_in_the_past_7_days()
        {
            GivenABusyNode();
            GivenChecks("blocked", Now.AddHours(-1), direct: 3);

            var network = Build().Network;

            network.FriendsDirect.Should().Be("1");
            network.FriendsRelayOnly.Should().Be("1");
            network.FriendsUnreachable.Should().Be("0");
            network.Udp.Should().BeTrue();
            network.PortVaries.Should().BeFalse();
            network.Relay.Should().Be("own");
        }

        [Test]
        public void what_the_transport_doesnt_know_should_be_null()
        {
            Mocker.GetMock<IPeerTransport>().Setup(t => t.GetNetworkStatus()).Throws(new TransportException(TransportFailure.Unavailable, "not running"));

            var network = Build().Network;

            network.Udp.Should().BeNull();
            network.PortVaries.Should().BeNull();
        }

        [Test]
        public void arr_apps_should_be_given_by_kind_and_major_and_minor_version_only()
        {
            Build().Apps.Should().BeEquivalentTo(new[]
            {
                new UsageApp { Kind = "radarr", Version = "unknown" },
                new UsageApp { Kind = "sonarr", Version = "4.0" }
            });
        }

        [Test]
        public void an_arr_app_should_be_asked_its_version_once_an_hour_at_most()
        {
            Build();
            Build();

            Mocker.GetMock<IArrV3Proxy>().Verify(p => p.GetVersion(It.Is<IArrConnection>(c => c.BaseUrl == SonarrUrl)), Times.Once());
        }

        [TestCase("Sonarr", "sonarr")]
        [TestCase("WhisparrV2", "whisparr")]
        [TestCase("WhisparrV3", "whisparr")]
        [TestCase("Lidarr", "lidarr")]
        [TestCase("SomethingElse", "other")]
        public void each_kind_of_app_should_have_its_name(string implementation, string kind)
        {
            UsageReportBuilder.Kind(implementation).Should().Be(kind);
        }

        [Test]
        public void features_in_use_should_be_said()
        {
            GivenABusyNode();

            var features = Build().Features;

            features.ShareLists.Should().BeTrue();
            features.HiddenTitles.Should().BeFalse();
            features.ShareEverything.Should().BeTrue();
            features.FetchAutomatically.Should().BeTrue();
            features.UploadLimit.Should().BeTrue();
            features.TotalUploadLimit.Should().BeFalse();
            features.DownloadLimit.Should().BeFalse();
            features.MaxUploads.Should().Be("default");
        }

        [TestCase(0, "none")]
        [TestCase(3, "default")]
        [TestCase(8, "other")]
        public void most_uploads_at_once_should_be_said_without_its_number(int maxUploads, string expected)
        {
            _settings.MaxUploads = maxUploads;

            Build().Features.MaxUploads.Should().Be(expected);
        }

        [Test]
        public void a_branch_of_the_owners_own_naming_should_be_other()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.Branch).Returns("johns-test-branch");

            Build().Nodearr.Branch.Should().Be("other");
        }

        [Test]
        public void a_node_that_moved_a_file_in_the_past_30_days_should_be_active()
        {
            GivenTransfers(Now.AddDays(-29), TransferDirection.Fetched, 1, 1000);

            Build().Active.Should().BeTrue();
        }

        [Test]
        public void a_node_that_moved_nothing_for_more_than_30_days_should_not_be_active()
        {
            GivenTransfers(Now.AddDays(-31), TransferDirection.Fetched, 1, 1000);

            Build().Active.Should().BeFalse();
        }

        [Test]
        public void a_node_answering_a_grab_now_should_be_active()
        {
            _activeTransfers.Add(new TransferJob { Added = Now.AddMinutes(-1) });

            Build().Active.Should().BeTrue();
        }

        [Test]
        public void the_id_should_stay_the_same_until_midnight_utc()
        {
            var morning = Subject.IdFor(new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc));
            var lastMoment = Subject.IdFor(new DateTime(2026, 11, 6, 23, 59, 59, 999, DateTimeKind.Utc));

            lastMoment.Should().Be(morning);
        }

        [Test]
        public void the_id_should_change_at_midnight_utc()
        {
            var before = Subject.IdFor(new DateTime(2026, 11, 6, 23, 59, 59, 999, DateTimeKind.Utc));
            var after = Subject.IdFor(new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc));

            after.Should().NotBe(before);
            Guid.TryParse(after, out _).Should().BeTrue();
        }

        [Test]
        public void the_id_should_follow_utc_not_the_local_clock()
        {
            // 23:30 on the 6th in UTC-5 is 04:30 on the 7th in UTC.
            var evening = new DateTimeOffset(2026, 11, 6, 23, 30, 0, TimeSpan.FromHours(-5)).UtcDateTime;

            Subject.IdFor(evening).Should().Be(Subject.IdFor(new DateTime(2026, 11, 7, 1, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void the_id_should_be_kept_in_the_database_so_a_restart_the_same_day_keeps_it()
        {
            var first = Subject.IdFor(Now);

            var repository = Mocker.Resolve<IConfigRepository>();
            repository.Get(UsageReportBuilder.IdKey).Value.Should().Be(first);
            repository.Get(UsageReportBuilder.IdDayKey).Value.Should().Be("2026-11-06");

            Subject.IdFor(Now.AddHours(2)).Should().Be(first);
        }

        [Test]
        public void a_container_should_be_a_docker_install()
        {
            Mocker.GetMock<IDeploymentInfoProvider>().SetupGet(d => d.PackageUpdateMechanism).Returns(UpdateMechanism.Docker);

            Subject.Install().Should().Be("docker");
        }

        [Test]
        public void a_development_build_should_say_so()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.StartUpFolder).Returns(OsInfo.IsWindows ? @"C:\src\Nodearr\_output\net10.0" : "/src/Nodearr/_output/net10.0");

            Subject.Install().Should().Be("development");
        }

        [Test]
        public void the_windows_installer_should_be_told_apart_from_a_zip()
        {
            WindowsOnly();

            Subject.Install().Should().Be("zip");

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(@"C:\Nodearr\bin\unins000.exe")).Returns(true);
            Subject.Install().Should().Be("installerTray");

            Mocker.GetMock<IRuntimeInfo>().SetupGet(r => r.IsWindowsService).Returns(true);
            Subject.Install().Should().Be("installerService");
        }

        [Test]
        public void an_archive_outside_windows_should_be_a_tarball()
        {
            PosixOnly();

            Subject.Install().Should().Be("tarball");
        }

        [Test]
        public void a_language_that_isnt_a_plain_code_should_be_other()
        {
            Mocker.GetMock<ILocalizationService>().Setup(l => l.GetLanguageIdentifier()).Returns("pt-BR");
            Build().Language.Should().Be("pt-BR");

            Mocker.GetMock<ILocalizationService>().Setup(l => l.GetLanguageIdentifier()).Returns("../../etc");
            Build().Language.Should().Be("other");
        }
    }
}

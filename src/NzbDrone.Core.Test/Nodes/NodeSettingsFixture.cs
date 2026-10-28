using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Nodes
{
    [TestFixture]
    public class NodeSettingsFixture : CoreTest
    {
        private Mock<IConfigService> _config;
        private InMemoryPeerTransport _transport;
        private List<TransferJob> _activeTransfers;
        private TransportOptions _environment;
        private Mock<IEventAggregator> _events;
        private Mock<IRelayProbe> _relayProbe;

        [SetUp]
        public void Setup()
        {
            // Behaves like the real store: what is written is what is read back.
            _config = new Mock<IConfigService>();
            _config.SetupAllProperties();
            _config.Object.TransportRelay = "n0";
            _config.Object.TransportPortMapping = true;
            _config.Object.TransferDirectTimeout = 20;
            _config.Object.CatalogSyncInterval = 15;
            _config.Object.TransferMaxDownloads = 3;
            _config.Object.TransferMaxUploads = 3;

            _transport = new InMemoryNetwork().Join("alice");
            _activeTransfers = new List<TransferJob>();
            _environment = new TransportOptions();
            _events = new Mock<IEventAggregator>();

            // A relay answers unless a test says otherwise.
            _relayProbe = new Mock<IRelayProbe>();
        }

        private NodeSettingsProvider Provider
        {
            get
            {
                var appFolder = new Mock<IAppFolderInfo>();

                appFolder.SetupGet(a => a.AppDataFolder).Returns(Path.Combine(TempFolder, "data"));

                return new NodeSettingsProvider(_config.Object, appFolder.Object, Options.Create(_environment));
            }
        }

        private NodeSettingsService Service
        {
            get
            {
                var transfers = new Mock<ITransferRepository>();

                transfers.Setup(t => t.Active()).Returns(() => _activeTransfers);

                return new NodeSettingsService(Provider, _config.Object, _transport, transfers.Object, _events.Object, _relayProbe.Object, TestLogger);
            }
        }

        private List<string> Problems(Action<NodeSettings> change)
        {
            var settings = Provider.Get();

            change(settings);

            return Service.Validate(settings).Select(f => f.PropertyName).ToList();
        }

        private NodeSettingsSaved Save(Action<NodeSettings> change)
        {
            var settings = Provider.Get();

            change(settings);
            Service.Validate(settings).Should().BeEmpty();

            return Service.Save(settings);
        }

        [Test]
        public void a_new_node_should_use_public_relays_any_port_and_a_folder_beside_its_data()
        {
            Provider.Get().Should().BeEquivalentTo(new NodeSettings { Relay = RelayMode.N0, Port = 0, PortMapping = true, DirectTimeoutSeconds = 20, UploadLimitKbps = 0, DownloadLimitKbps = 0, MaxDownloads = 3, MaxUploads = 3, CatalogSyncInterval = 15 });
            Provider.FixedByEnvironment().Should().BeEmpty();
            Provider.NodeName.Should().Be(Environment.MachineName);
            Provider.DownloadFolder.Should().Be(Path.Combine(TempFolder, "data", "downloads"));
        }

        // Sonarr's limits for its RSS Sync Interval.
        [TestCase(0, true)]
        [TestCase(10, true)]
        [TestCase(15, true)]
        [TestCase(120, true)]
        [TestCase(9, false)]
        [TestCase(121, false)]
        [TestCase(-1, false)]
        public void the_catalog_sync_interval_should_be_10_to_120_minutes_or_0(int minutes, bool allowed)
        {
            Problems(s => s.CatalogSyncInterval = minutes).Contains(nameof(NodeSettings.CatalogSyncInterval)).Should().Be(!allowed);
        }

        [Test]
        public void a_new_catalog_sync_interval_should_be_saved_and_the_schedule_told()
        {
            Save(s => s.CatalogSyncInterval = 30);

            Provider.Get().CatalogSyncInterval.Should().Be(30);
            _events.Verify(e => e.PublishEvent(It.IsAny<ConfigSavedEvent>()), Times.Once());
        }

        [Test]
        public void other_settings_should_not_disturb_the_schedule()
        {
            Save(s => s.UploadLimitKbps = 500);

            _events.Verify(e => e.PublishEvent(It.IsAny<ConfigSavedEvent>()), Times.Never());
        }

        [Test]
        public void what_was_saved_should_be_what_is_used()
        {
            Save(s =>
            {
                s.NodeName = "  John  ";
                s.UploadLimitKbps = 500;
                s.TotalUploadLimitKbps = 1500;
                s.DownloadLimitKbps = 2000;
                s.MaxDownloads = 5;
                s.MaxUploads = 2;
            });

            Provider.NodeName.Should().Be("John");
            Provider.Get().MaxDownloads.Should().Be(5);
            Provider.Get().MaxUploads.Should().Be(2);
            Provider.Get().UploadLimitKbps.Should().Be(500);
            Provider.Get().TotalUploadLimitKbps.Should().Be(1500);
            Provider.Get().DownloadLimitKbps.Should().Be(2000);
        }

        [Test]
        public void the_environment_should_win_over_what_was_saved()
        {
            _config.Object.NodeName = "Saved";
            _config.Object.TransportPort = 1111;
            _config.Object.TransportPortMapping = true;
            _config.Object.TransferDownloadLimit = 100;
            _config.Object.TransferTotalUploadLimit = 100;
            _environment = new TransportOptions { NodeName = "From the environment", Port = 6633, DisablePortMapping = true, Relay = "disabled", DownloadFolder = Path.Combine(TempFolder, "mounted"), DownloadLimitKbps = 4000, TotalUploadLimitKbps = 3000, MaxDownloads = 0, MaxUploads = 0 };

            Provider.Get().Should().Match<NodeSettings>(s => s.NodeName == "From the environment" && s.Port == 6633 && !s.PortMapping && s.Relay == RelayMode.Disabled && s.DownloadLimitKbps == 4000 && s.TotalUploadLimitKbps == 3000 && s.MaxDownloads == 0 && s.MaxUploads == 0);
            Provider.DownloadFolder.Should().Be(Path.Combine(TempFolder, "mounted"));
            Provider.FixedByEnvironment().Should().BeEquivalentTo("NodeName", "Port", "PortMapping", "Relay", "DownloadFolder", "DownloadLimitKbps", "TotalUploadLimitKbps", "MaxDownloads", "MaxUploads");
        }

        [Test]
        public void a_relay_setting_nobody_can_read_should_not_switch_relays_off()
        {
            _environment = new TransportOptions { Relay = "disbaled" };

            Provider.Get().Relay.Should().Be(RelayMode.N0);
        }

        [Test]
        public void a_setting_fixed_by_the_environment_should_be_left_alone_and_said_to_be()
        {
            _environment = new TransportOptions { Port = 6633 };

            var saved = Service.Save(new NodeSettings { Relay = RelayMode.N0, PortMapping = true, DirectTimeoutSeconds = 20, Port = 7000, NodeName = "John" });

            saved.Ignored.Should().Equal("Port");
            saved.Settings.Port.Should().Be(6633);
            saved.Settings.NodeName.Should().Be("John");
            _config.Object.TransportPort.Should().Be(0);
        }

        [Test]
        public void a_setting_fixed_by_the_environment_should_not_be_judged()
        {
            _environment = new TransportOptions { Port = 80, DirectTimeoutSeconds = 1 };

            Service.Validate(Provider.Get()).Should().BeEmpty();
        }

        [TestCase(80)]
        [TestCase(1023)]
        [TestCase(65536)]
        [TestCase(-1)]
        public void a_port_outside_the_usable_range_should_be_refused(int port)
        {
            Problems(s => s.Port = port).Should().Equal("Port");
        }

        [Test]
        public void a_port_something_else_is_using_should_be_refused()
        {
            using var taken = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            taken.Bind(new IPEndPoint(IPAddress.Any, 0));

            var port = ((IPEndPoint)taken.LocalEndPoint).Port;

            Problems(s => s.Port = port).Should().Equal("Port");
        }

        [Test]
        public void the_port_already_in_use_by_this_node_should_be_allowed()
        {
            using var taken = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            taken.Bind(new IPEndPoint(IPAddress.Any, 0));
            _config.Object.TransportPort = ((IPEndPoint)taken.LocalEndPoint).Port;

            // Saving something else must not trip over the node's own port.
            Problems(s => s.NodeName = "John").Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("relay.example.com")]
        [TestCase("ftp://relay.example.com")]
        public void a_relay_of_ones_own_should_need_a_real_address(string url)
        {
            Problems(s =>
            {
                s.Relay = RelayMode.Custom;
                s.RelayUrl = url;
            }).Should().Equal("RelayUrl");
        }

        [Test]
        public void a_relay_address_should_not_matter_unless_it_is_used()
        {
            Problems(s => s.RelayUrl = "not an address").Should().BeEmpty();
            Problems(s =>
            {
                s.Relay = RelayMode.Custom;
                s.RelayUrl = "https://relay.example.com";
            }).Should().BeEmpty();
        }

        [Test]
        public void a_new_relay_of_ones_own_that_does_not_answer_should_be_refused_with_the_reason()
        {
            _relayProbe.Setup(p => p.Check(It.IsAny<Uri>())).Returns("No relay answered at relay.example.com: connection refused");

            var failures = Service.Validate(new NodeSettings { Relay = RelayMode.Custom, RelayUrl = "https://relay.example.com", DirectTimeoutSeconds = 20 });

            failures.Should().ContainSingle();
            failures[0].PropertyName.Should().Be("RelayUrl");
            failures[0].ErrorMessage.Should().Contain("connection refused");
            _relayProbe.Verify(p => p.Check(new Uri("https://relay.example.com")), Times.Once());
        }

        [Test]
        public void a_relay_already_in_use_should_not_be_asked_again()
        {
            _config.Object.TransportRelay = "custom";
            _config.Object.TransportRelayUrl = "https://relay.example.com";
            _relayProbe.Setup(p => p.Check(It.IsAny<Uri>())).Returns("No relay answered at relay.example.com: timed out");

            // A relay that is down for a moment does not stop the rest of the page being saved.
            Problems(s => s.NodeName = "John").Should().BeEmpty();
            _relayProbe.Verify(p => p.Check(It.IsAny<Uri>()), Times.Never());

            // A new address is asked, and so is the old one when relays of one's own are turned on again.
            Problems(s => s.RelayUrl = "https://other.example.com").Should().Equal("RelayUrl");
            _config.Object.TransportRelay = "n0";
            Problems(s => s.Relay = RelayMode.Custom).Should().Equal("RelayUrl");
        }

        [Test]
        public void a_relay_address_should_not_matter_when_the_environment_fixes_another_relay()
        {
            _environment = new TransportOptions { Relay = "disabled" };

            Problems(s =>
            {
                s.Relay = RelayMode.Custom;
                s.RelayUrl = "relay.example.com";
            }).Should().BeEmpty();

            _relayProbe.Verify(p => p.Check(It.IsAny<Uri>()), Times.Never());
        }

        [Test]
        public void a_relay_address_of_the_wrong_shape_should_not_be_asked()
        {
            Problems(s =>
            {
                s.Relay = RelayMode.Custom;
                s.RelayUrl = "relay.example.com";
            }).Should().Equal("RelayUrl");

            _relayProbe.Verify(p => p.Check(It.IsAny<Uri>()), Times.Never());
        }

        [TestCase(4)]
        [TestCase(121)]
        public void a_wait_that_is_too_short_or_too_long_should_be_refused(int seconds)
        {
            Problems(s => s.DirectTimeoutSeconds = seconds).Should().Equal("DirectTimeoutSeconds");
        }

        [TestCase(1)]
        [TestCase(15)]
        [TestCase(-5)]
        public void an_upload_limit_too_low_to_work_should_be_refused(int limit)
        {
            Problems(s => s.UploadLimitKbps = limit).Should().Equal("UploadLimitKbps");
        }

        [TestCase(1)]
        [TestCase(15)]
        [TestCase(-5)]
        public void a_total_upload_limit_too_low_to_work_should_be_refused(int limit)
        {
            Problems(s => s.TotalUploadLimitKbps = limit).Should().Equal("TotalUploadLimitKbps");
        }

        [TestCase(0)]
        [TestCase(16)]
        [TestCase(100000)]
        public void no_total_upload_limit_or_one_that_works_should_be_allowed(int limit)
        {
            Problems(s => s.TotalUploadLimitKbps = limit).Should().BeEmpty();
        }

        [TestCase(-1)]
        [TestCase(101)]
        public void a_number_of_downloads_at_once_out_of_range_should_be_refused(int count)
        {
            Problems(s => s.MaxDownloads = count).Should().Equal("MaxDownloads");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(100)]
        public void no_limit_on_downloads_at_once_or_one_in_range_should_be_allowed(int count)
        {
            Problems(s => s.MaxDownloads = count).Should().BeEmpty();
        }

        [TestCase(-1)]
        [TestCase(101)]
        public void a_number_of_uploads_at_once_out_of_range_should_be_refused(int count)
        {
            Problems(s => s.MaxUploads = count).Should().Equal("MaxUploads");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(100)]
        public void no_limit_on_uploads_at_once_or_one_in_range_should_be_allowed(int count)
        {
            Problems(s => s.MaxUploads = count).Should().BeEmpty();
        }

        [TestCase(1)]
        [TestCase(15)]
        [TestCase(-5)]
        public void a_download_limit_too_low_to_work_should_be_refused(int limit)
        {
            Problems(s => s.DownloadLimitKbps = limit).Should().Equal("DownloadLimitKbps");
        }

        [TestCase(0)]
        [TestCase(16)]
        [TestCase(100000)]
        public void no_download_limit_or_one_that_works_should_be_allowed(int limit)
        {
            Problems(s => s.DownloadLimitKbps = limit).Should().BeEmpty();
        }

        [Test]
        public void a_name_should_be_short_and_on_one_line()
        {
            Problems(s => s.NodeName = new string('x', 65)).Should().Equal("NodeName");
            Problems(s => s.NodeName = "John\nSmith").Should().Equal("NodeName");
            Problems(s => s.NodeName = new string('x', 64)).Should().BeEmpty();
        }

        [Test]
        public void a_download_folder_should_be_a_whole_path_to_a_folder_that_can_be_written()
        {
            Problems(s => s.DownloadFolder = "downloads").Should().Equal("DownloadFolder");
            Problems(s => s.DownloadFolder = Path.GetPathRoot(TempFolder)).Should().Equal("DownloadFolder");

            var wanted = Path.Combine(TempFolder, "new", "downloads");

            Problems(s => s.DownloadFolder = wanted).Should().BeEmpty();
            Directory.Exists(wanted).Should().BeTrue();
            Directory.GetFileSystemEntries(wanted).Should().BeEmpty();
        }

        [Test]
        public void the_download_folder_should_not_move_while_transfers_are_under_way()
        {
            _activeTransfers.Add(new TransferJob());

            Problems(s => s.DownloadFolder = Path.Combine(TempFolder, "elsewhere")).Should().Equal("DownloadFolder");

            // Everything else can still be changed.
            Problems(s => s.NodeName = "John").Should().BeEmpty();
        }

        [Test]
        public void the_transport_should_restart_only_for_what_it_is_given_when_it_starts()
        {
            Save(s => s.NodeName = "John").TransportRestarted.Should().BeFalse();
            Save(s => s.DownloadFolder = Path.Combine(TempFolder, "elsewhere")).TransportRestarted.Should().BeFalse();
            Save(s => s.MaxDownloads = 1).TransportRestarted.Should().BeFalse();
            _transport.Restarts.Should().Be(0);

            Save(s => s.UploadLimitKbps = 500).Should().Match<NodeSettingsSaved>(r => r.TransportRestarted && r.TransportRunning);
            Save(s => s.DirectTimeoutSeconds = 30).TransportRestarted.Should().BeTrue();
            Save(s => s.PortMapping = false).TransportRestarted.Should().BeTrue();
            Save(s => s.Relay = RelayMode.Disabled).TransportRestarted.Should().BeTrue();
            Save(s => s.DownloadLimitKbps = 2000).TransportRestarted.Should().BeTrue();
            Save(s => s.TotalUploadLimitKbps = 2000).TransportRestarted.Should().BeTrue();
            Save(s => s.MaxUploads = 1).TransportRestarted.Should().BeTrue();
            _transport.Restarts.Should().Be(7);
        }

        [Test]
        public void saving_the_same_settings_again_should_change_nothing()
        {
            Save(s => s.UploadLimitKbps = 500);

            var again = Save(_ => { });

            again.TransportRestarted.Should().BeFalse();
            _transport.Restarts.Should().Be(1);
        }

        [Test]
        public void failures_should_name_the_setting_so_the_form_can_point_at_it()
        {
            var failures = Service.Validate(new NodeSettings { Relay = RelayMode.Custom, Port = 80, DirectTimeoutSeconds = 1, UploadLimitKbps = 3, TotalUploadLimitKbps = 3, DownloadLimitKbps = 3, MaxDownloads = -1, MaxUploads = 101, NodeName = "a\tb", DownloadFolder = "relative" });

            failures.Select(f => f.PropertyName).Should().BeEquivalentTo("NodeName", "RelayUrl", "Port", "DirectTimeoutSeconds", "UploadLimitKbps", "TotalUploadLimitKbps", "DownloadLimitKbps", "MaxDownloads", "MaxUploads", "DownloadFolder");
            failures.Should().OnlyContain((ValidationFailure f) => f.ErrorMessage.Length > 10);
        }
    }
}

using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.Commands;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class CheckUpdateServiceFixture : CoreTest<CheckUpdateService>
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

        private Dictionary<string, string> _config;
        private UpdateManifestResult _fetched;

        [SetUp]
        public void Setup()
        {
            _config = new Dictionary<string, string>();
            Mocker.GetMock<IConfigRepository>().Setup(c => c.Get(It.IsAny<string>()))
                  .Returns<string>(k => _config.TryGetValue(k, out var v) ? new Config { Key = k, Value = v } : null);
            Mocker.GetMock<IConfigRepository>().Setup(c => c.Upsert(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((k, v) => _config[k] = v);

            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ContactNodearr).Returns(true);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.UpdateCheck).Returns(true);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.Branch).Returns("main");

            GivenManifest(Newer());
            Mocker.GetMock<IUpdatePackageProvider>().Setup(c => c.Fetch(It.IsAny<string>())).Returns(() => _fetched);
        }

        // Newer than any build, release or development: a development build is 10.0.
        private static string Newer() => $"{CheckUpdateService.CurrentVersion.Major + 1}.0.0";

        private void GivenManifest(string version, int rollout = 100)
        {
            var manifest = new UpdateManifest
            {
                Branch = "main",
                Version = SemanticVersion.Parse(version),
                Released = Now.AddDays(-1),
                Rollout = rollout,
                Image = "ghcr.io/nodearr/nodearr:" + version
            };

            manifest.Packages[CheckUpdateService.Platform] = new UpdateManifestPackage
            {
                File = $"Nodearr.{version}.{CheckUpdateService.Platform}.zip",
                Url = "https://github.com/Nodearr/Nodearr/releases/download/v1/x.zip",
                Size = 10,
                Sha256 = new string('a', 64)
            };

            _fetched = new UpdateManifestResult { Outcome = UpdateManifestOutcome.Verified, Manifest = manifest };
        }

        [Test]
        public void should_offer_a_newer_version_with_the_package_for_this_platform()
        {
            var status = Subject.Check();

            status.Outcome.Should().Be(UpdateCheckOutcome.Checked);
            status.Available.SemanticVersion.ToString().Should().Be(Newer());
            status.Available.FileName.Should().EndWith($".{CheckUpdateService.Platform}.zip");
            status.Available.Hash.Should().Be(new string('a', 64));
        }

        [Test]
        public void should_ignore_the_version_running()
        {
            GivenManifest(CheckUpdateService.CurrentVersion.ToString());

            Subject.Check().Available.Should().BeNull();
        }

        [Test]
        public void should_ignore_an_older_version_so_a_stale_or_replayed_manifest_cannot_take_a_node_back()
        {
            GivenManifest("0.1.0");

            var status = Subject.Check();

            status.Outcome.Should().Be(UpdateCheckOutcome.Checked);
            status.Available.Should().BeNull();
        }

        [Test]
        public void should_offer_a_release_without_a_package_for_this_platform_and_say_so()
        {
            _fetched.Manifest.Packages.Clear();

            var available = Subject.Check().Available;

            available.Should().NotBeNull();
            available.Url.Should().BeNull();
        }

        [Test]
        public void should_offer_nothing_from_a_manifest_with_a_bad_signature()
        {
            Subject.Check();

            _fetched = new UpdateManifestResult { Outcome = UpdateManifestOutcome.BadSignature };
            var status = Subject.Check();

            status.Outcome.Should().Be(UpdateCheckOutcome.BadSignature);
            status.Available.Should().BeNull();
            Subject.AvailableUpdate().Should().BeNull();
        }

        [Test]
        public void should_keep_what_it_knew_when_the_manifest_cannot_be_reached()
        {
            Subject.Check();

            _fetched = new UpdateManifestResult { Outcome = UpdateManifestOutcome.Unavailable };
            var status = Subject.Check();

            status.Outcome.Should().Be(UpdateCheckOutcome.Unavailable);
            status.Available.SemanticVersion.ToString().Should().Be(Newer());
        }

        [Test]
        public void should_not_ask_when_contact_nodearr_com_is_off_whatever_check_for_updates_says()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ContactNodearr).Returns(false);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.UpdateCheck).Returns(true);

            Subject.Check().Outcome.Should().Be(UpdateCheckOutcome.Off);

            Mocker.GetMock<IUpdatePackageProvider>().Verify(c => c.Fetch(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_ask_when_check_for_updates_is_off()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.UpdateCheck).Returns(false);

            Subject.Check().Outcome.Should().Be(UpdateCheckOutcome.Off);

            Mocker.GetMock<IUpdatePackageProvider>().Verify(c => c.Fetch(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_forget_an_update_once_checking_is_turned_off()
        {
            Subject.Check();

            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ContactNodearr).Returns(false);

            Subject.Check();
            Subject.AvailableUpdate().Should().BeNull();
        }

        [TestCase(30, 30, true)]
        [TestCase(31, 30, false)]
        [TestCase(1, 1, true)]
        [TestCase(100, 99, false)]
        public void should_take_a_release_only_when_its_rollout_reaches_this_node(int bucket, int rollout, bool takes)
        {
            _config["updaterolloutbucket"] = bucket.ToString();
            GivenManifest(Newer(), rollout);

            (Subject.Check().Available != null).Should().Be(takes);
        }

        [Test]
        public void should_pick_its_rollout_number_once_and_keep_it()
        {
            GivenManifest(Newer(), 50);

            Subject.Check();
            var picked = _config["updaterolloutbucket"];

            Subject.Check();
            Subject.Check();

            _config["updaterolloutbucket"].Should().Be(picked);
            int.Parse(picked).Should().BeInRange(1, 100);
        }

        [Test]
        public void should_not_pick_a_rollout_number_when_every_node_takes_the_release()
        {
            Subject.Check();

            _config.Should().NotContainKey("updaterolloutbucket");
        }

        [Test]
        public void should_remember_when_it_first_heard_of_a_version()
        {
            var first = Subject.Evaluate(_fetched, Now);
            var later = Subject.Evaluate(_fetched, Now.AddDays(3));

            first.AvailableSince.Should().Be(Now);
            later.AvailableSince.Should().Be(Now);
        }

        [Test]
        public void should_start_counting_again_for_a_later_version()
        {
            Subject.Evaluate(_fetched, Now);

            GivenManifest($"{CheckUpdateService.CurrentVersion.Major + 1}.1.0");
            Subject.Evaluate(_fetched, Now.AddDays(3)).AvailableSince.Should().Be(Now.AddDays(3));
        }

        [Test]
        public void should_queue_the_installer_when_a_newer_version_is_found()
        {
            Subject.Execute(new ApplicationUpdateCheckCommand { Trigger = CommandTrigger.Manual });

            // Checking now does not mean installing now: the installer waits as for any automatic update.
            Mocker.GetMock<IManageCommandQueue>().Verify(c => c.Push(It.IsAny<ApplicationUpdateCommand>(), It.IsAny<CommandPriority>(), CommandTrigger.Scheduled), Times.Once());
        }

        [Test]
        public void should_not_queue_the_installer_when_there_is_nothing_newer()
        {
            GivenManifest("0.1.0");

            Subject.Execute(new ApplicationUpdateCheckCommand());

            Mocker.GetMock<IManageCommandQueue>().Verify(c => c.Push(It.IsAny<ApplicationUpdateCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }
    }
}

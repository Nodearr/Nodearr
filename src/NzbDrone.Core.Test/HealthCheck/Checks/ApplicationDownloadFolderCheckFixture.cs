using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class ApplicationDownloadFolderCheckFixture : CoreTest<ApplicationDownloadFolderCheck>
    {
        private string _complete;
        private Dictionary<string, object> _tokens;
        private Mock<IApplication> _sonarr;

        [SetUp]
        public void Setup()
        {
            _complete = Path.Combine(TempFolder, "complete");

            Mocker.GetMock<ITransferFolders>().SetupGet(f => f.Complete).Returns(_complete);

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Callback<string, Dictionary<string, object>>((_, tokens) => _tokens = tokens)
                  .Returns("Some Warning Message");

            _sonarr = new Mock<IApplication>();
            _sonarr.SetupGet(a => a.Definition).Returns(new ApplicationDefinition { Id = 1, Name = "Sonarr" });

            Mocker.GetMock<IApplicationFactory>()
                  .Setup(f => f.GetAvailableProviders())
                  .Returns(new List<IApplication> { _sonarr.Object });
        }

        // The app "sees" the folder when the marker Nodearr just made is really there.
        private void GivenTheAppLooksAt(Func<string, string> where)
        {
            _sonarr.Setup(a => a.LookForFolder(_complete, It.IsAny<string>()))
                   .Returns<string, string>((folder, marker) => new FolderVisibility
                   {
                       Found = Directory.Exists(Path.Combine(where(folder), marker)),
                       LookedAt = where(folder)
                   });
        }

        [Test]
        public void should_be_ok_when_the_app_sees_the_marker_nodearr_put_there()
        {
            GivenTheAppLooksAt(folder => folder);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_warn_when_the_app_looks_somewhere_else()
        {
            GivenTheAppLooksAt(_ => Path.Combine(TempFolder, "another-folder"));

            Subject.Check().ShouldBeWarning();

            _tokens["applicationName"].Should().Be("Sonarr");
            _tokens["path"].Should().Be(_complete);
            _tokens["lookedAt"].Should().Be(Path.Combine(TempFolder, "another-folder"));
        }

        [Test]
        public void should_leave_no_marker_behind()
        {
            GivenTheAppLooksAt(folder => folder);

            Subject.Check();

            Directory.GetFileSystemEntries(_complete).Should().BeEmpty();
        }

        [Test]
        public void should_say_nothing_when_the_app_cannot_be_asked()
        {
            _sonarr.Setup(a => a.LookForFolder(It.IsAny<string>(), It.IsAny<string>())).Throws(new InvalidOperationException("Sonarr is down"));

            Subject.Check().ShouldBeOk();
            Directory.GetFileSystemEntries(_complete).Should().BeEmpty();
        }

        [Test]
        public void should_be_ok_and_touch_nothing_when_there_are_no_apps()
        {
            Mocker.GetMock<IApplicationFactory>().Setup(f => f.GetAvailableProviders()).Returns(new List<IApplication>());

            Subject.Check().ShouldBeOk();
            Directory.Exists(_complete).Should().BeFalse();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Model;
using NzbDrone.Common.Processes;
using NzbDrone.Test.Common;
using NzbDrone.Update.UpdateEngine;

namespace NzbDrone.Update.Test
{
    [TestFixture]
    public class InstallUpdateServiceFixture : TestBase<InstallUpdateService>
    {
        private const int _processId = 12;
        private string _targetFolder = @"C:\NzbDrone\".AsOsAgnostic();

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAppFolderInfo>()
                  .Setup(c => c.TempFolder).Returns(@"C:\Temp\");

            Subject.TransportWait = TimeSpan.Zero;
            Subject.ExternalRestartWait = TimeSpan.Zero;
        }

        private void GivenTargetFolderExists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(_targetFolder))
                  .Returns(true);
        }

        private void GivenProcessExists()
        {
            Mocker.GetMock<IProcessProvider>()
                  .Setup(c => c.Exists(_processId))
                  .Returns(true);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void update_should_throw_target_folder_is_blank(string target)
        {
            Assert.Throws<ArgumentException>(() => Subject.Start(target, _processId))
            .Message.Should().StartWith("Target folder can not be null or empty");
        }

        [Test]
        public void update_should_throw_if_target_folder_doesnt_exist()
        {
            Assert.Throws<DirectoryNotFoundException>(() => Subject.Start(_targetFolder, _processId))
            .Message.Should().StartWith("Target folder doesn't exist");
        }

        [Test]
        public void update_should_throw_if_update_folder_doesnt_exist()
        {
            const string sandboxFolder = @"C:\Temp\NzbDrone_update\nzbdrone";

            GivenTargetFolderExists();
            GivenProcessExists();

            Mocker.GetMock<IDiskProvider>()
               .Setup(c => c.FolderExists(sandboxFolder))
               .Returns(false);

            Assert.Throws<DirectoryNotFoundException>(() => Subject.Start(_targetFolder, _processId))
                .Message.Should().StartWith("Update folder doesn't exist");
        }

        [Test]
        public void update_should_throw_if_process_is_zero()
        {
            GivenTargetFolderExists();

            Assert.Throws<ArgumentException>(() => Subject.Start(_targetFolder, 0))
            .Message.Should().StartWith("Invalid process ID");
        }

        [Test]
        public void update_should_throw_if_process_id_doesnt_exist()
        {
            GivenTargetFolderExists();

            Assert.Throws<ArgumentException>(() => Subject.Start(_targetFolder, _processId))
            .Message.Should().StartWith("Process with ID doesn't exist");
        }

        private void GivenAnUpdateToInstall()
        {
            GivenTargetFolderExists();
            GivenProcessExists();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(Mocker.GetMock<IAppFolderInfo>().Object.GetUpdatePackageFolder()))
                  .Returns(true);

            Mocker.GetMock<IProcessProvider>()
                  .Setup(c => c.FindProcessByName(It.IsAny<string>()))
                  .Returns(new List<ProcessInfo>());
        }

        [Test]
        public void should_keep_the_new_version_when_it_answers()
        {
            GivenAnUpdateToInstall();
            Mocker.GetMock<IStartupCheck>().Setup(c => c.Answers(It.IsAny<TimeSpan>())).Returns(true);

            Subject.Start(_targetFolder, _processId);

            Mocker.GetMock<IDiskTransferService>().Verify(c => c.MirrorFolder(Mocker.GetMock<IAppFolderInfo>().Object.GetUpdatePackageFolder(), _targetFolder), Times.Once());
            Mocker.GetMock<IBackupAndRestore>().Verify(c => c.Restore(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IBackupAppData>().Verify(c => c.Restore(), Times.Never());
        }

        [Test]
        public void should_wait_three_minutes_for_the_new_version_to_answer()
        {
            GivenAnUpdateToInstall();
            Mocker.GetMock<IStartupCheck>().Setup(c => c.Answers(It.IsAny<TimeSpan>())).Returns(true);

            Subject.Start(_targetFolder, _processId);

            Mocker.GetMock<IStartupCheck>().Verify(c => c.Answers(TimeSpan.FromMinutes(3)), Times.Once());
        }

        [Test]
        public void should_put_the_old_version_and_its_data_back_when_the_new_one_does_not_answer()
        {
            GivenAnUpdateToInstall();
            Mocker.GetMock<IStartupCheck>().SetupSequence(c => c.Answers(It.IsAny<TimeSpan>())).Returns(false).Returns(true);

            Subject.Start(_targetFolder, _processId);

            Mocker.GetMock<IBackupAndRestore>().Verify(c => c.Restore(_targetFolder), Times.Once());
            Mocker.GetMock<IBackupAppData>().Verify(c => c.Restore(), Times.Once());
            Mocker.GetMock<IStartupCheck>().Verify(c => c.Answers(It.IsAny<TimeSpan>()), Times.Exactly(2));
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_stop_only_the_transport_of_this_installation()
        {
            GivenAnUpdateToInstall();
            Mocker.GetMock<IStartupCheck>().Setup(c => c.Answers(It.IsAny<TimeSpan>())).Returns(true);

            var ours = new ProcessInfo { Id = 40, Name = "nodearr-transport", StartPath = Path.Combine(_targetFolder, "nodearr-transport") };
            var another = new ProcessInfo { Id = 41, Name = "nodearr-transport", StartPath = @"D:\Other\nodearr-transport".AsOsAgnostic() };

            Mocker.GetMock<IProcessProvider>()
                  .Setup(c => c.FindProcessByName("nodearr-transport"))
                  .Returns(new List<ProcessInfo> { ours, another });

            Subject.Start(_targetFolder, _processId);

            if (OsInfo.IsWindows)
            {
                Mocker.GetMock<IProcessProvider>().Verify(c => c.Kill(40), Times.Once());
            }

            Mocker.GetMock<IProcessProvider>().Verify(c => c.Kill(41), Times.Never());
        }
    }
}

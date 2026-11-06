using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Model;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.Commands;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class UpdateServiceFixture : CoreTest<InstallUpdateService>
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

        private string _sandboxFolder;
        private UpdatePackage _updatePackage;
        private Dictionary<string, string> _config;

        [SetUp]
        public void Setup()
        {
            var fileName = OsInfo.IsWindows ? "Nodearr.10.1.0.win-x64.zip" : "Nodearr.10.1.0.linux-x64.tar.gz";

            _updatePackage = new UpdatePackage
            {
                FileName = fileName,
                Url = "https://github.com/Nodearr/Nodearr/releases/download/v10.1.0/" + fileName,
                Version = new Version(10, 1, 0, 0),
                SemanticVersion = SemanticVersion.Parse("10.1.0"),
                Size = 1,
                Hash = new string('a', 64)
            };

            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.TempFolder).Returns(TempFolder);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.StartUpFolder).Returns(@"C:\ProgramData\Nodearr\bin".AsOsAgnostic);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.AppDataFolder).Returns(@"C:\ProgramData\Nodearr".AsOsAgnostic);

            Mocker.GetMock<ICheckUpdateService>().Setup(c => c.AvailableUpdate()).Returns(_updatePackage);
            Mocker.GetMock<IVerifyUpdates>().Setup(c => c.Verify(It.IsAny<UpdatePackage>(), It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IUpdateInstallability>().Setup(c => c.Kind(It.IsAny<UpdatePackage>())).Returns(UpdateInstallKind.BuiltIn);

            Mocker.GetMock<IProcessProvider>().Setup(c => c.GetCurrentProcess()).Returns(new ProcessInfo { Id = 12 });
            Mocker.GetMock<IRuntimeInfo>().Setup(c => c.ExecutingApplication).Returns(@"C:\Test\Nodearr.exe");

            Mocker.GetMock<IConfigFileProvider>().SetupGet(s => s.UpdateAutomatically).Returns(true);

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FolderWritable(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.Is<string>(s => s.EndsWith("Nodearr.Update".ProcessNameToExe()))))
                  .Returns(true);

            _config = new Dictionary<string, string>();
            Mocker.GetMock<IConfigRepository>().Setup(c => c.Get(It.IsAny<string>()))
                  .Returns<string>(k => _config.TryGetValue(k, out var v) ? new Config { Key = k, Value = v } : null);
            Mocker.GetMock<IConfigRepository>().Setup(c => c.Upsert(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((k, v) => _config[k] = v);

            _sandboxFolder = Mocker.GetMock<IAppFolderInfo>().Object.GetUpdateSandboxFolder();

            // The package is downloaded into the sandbox for real, from a mocked response.
            Directory.CreateDirectory(_sandboxFolder);

            Subject.Now = () => Now;
        }

        private static ApplicationUpdateCommand Manual() => new ApplicationUpdateCommand { Trigger = CommandTrigger.Manual };

        private static ApplicationUpdateCommand Scheduled() => new ApplicationUpdateCommand { Trigger = CommandTrigger.Scheduled };

        private void GivenBusy()
        {
            Mocker.GetMock<IUpdateQuietMoment>().Setup(c => c.Busy()).Returns("1 transfer(s) queued or under way");
        }

        private void VerifyUpdaterStarted(Times times)
        {
            Mocker.GetMock<IProcessProvider>()
                .Verify(c => c.Start(It.IsAny<string>(), It.Is<string>(s => s.StartsWith("12")), null, null, null), times);
        }

        private void VerifyDownloaded(Times times)
        {
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == _updatePackage.Url)), times);
        }

        private void GivenInstallScript(string path)
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(s => s.UpdateMechanism).Returns(UpdateMechanism.Script);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(s => s.UpdateScriptPath).Returns(path);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(path, StringComparison.Ordinal)).Returns(true);
        }

        [Test]
        public void should_delete_sandbox_before_update_if_folder_exists()
        {
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FolderExists(_sandboxFolder)).Returns(true);

            Subject.Execute(Manual());

            Mocker.GetMock<IDiskProvider>().Verify(c => c.DeleteFolder(_sandboxFolder, true));
        }

        [Test]
        public void should_download_the_package_with_the_plain_user_agent_and_no_cookies()
        {
            Subject.Execute(Manual());

            var url = _updatePackage.Url;

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == url && r.UseSimplifiedUserAgent && !r.StoreRequestCookie && !r.StoreResponseCookie)), Times.Once());
        }

        [Test]
        public void should_extract_update_package()
        {
            var updateArchive = Path.Combine(_sandboxFolder, _updatePackage.FileName);

            Subject.Execute(Manual());

            Mocker.GetMock<IArchiveService>().Verify(c => c.Extract(updateArchive, _sandboxFolder));
        }

        [Test]
        public void should_copy_update_client_to_root_of_sandbox()
        {
            var updateClientFolder = Mocker.GetMock<IAppFolderInfo>().Object.GetUpdateClientFolder();

            Subject.Execute(Manual());

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(c => c.TransferFolder(updateClientFolder, _sandboxFolder, TransferMode.Move));
        }

        [Test]
        public void should_start_update_client_if_updater_exists()
        {
            Subject.Execute(Manual());

            VerifyUpdaterStarted(Times.Once());
        }

        [Test]
        public void should_fail_if_the_package_has_no_updater()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.Is<string>(s => s.EndsWith("Nodearr.Update".ProcessNameToExe()))))
                  .Returns(false);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(Manual()));

            VerifyUpdaterStarted(Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_return_without_error_or_warnings_when_no_updates_are_available()
        {
            Mocker.GetMock<ICheckUpdateService>().Setup(c => c.AvailableUpdate()).Returns<UpdatePackage>(null);

            Subject.Execute(Manual());

            VerifyDownloaded(Times.Never());
            ExceptionVerification.AssertNoUnexpectedLogs();
        }

        [Test]
        public void should_not_extract_if_verification_fails()
        {
            Mocker.GetMock<IVerifyUpdates>().Setup(c => c.Verify(It.IsAny<UpdatePackage>(), It.IsAny<string>())).Returns(false);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(Manual()));

            Mocker.GetMock<IArchiveService>().Verify(v => v.Extract(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            VerifyUpdaterStarted(Times.Never());
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_run_script_if_configured()
        {
            const string scriptPath = "/tmp/nzbdrone/update.sh";

            GivenInstallScript(scriptPath);

            Subject.Execute(Manual());

            Mocker.GetMock<IProcessProvider>().Verify(v => v.Start(scriptPath, It.IsAny<string>(), null, null, null), Times.Once());
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_throw_if_script_is_not_set()
        {
            GivenInstallScript("");

            Assert.Throws<CommandFailedException>(() => Subject.Execute(Manual()));

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_log_error_when_app_data_is_child_of_startup_folder()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.StartUpFolder).Returns(@"C:\NzbDrone".AsOsAgnostic);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.AppDataFolder).Returns(@"C:\NzbDrone\AppData".AsOsAgnostic);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(Manual()));
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_install_from_the_program_folder_inside_the_data_folder_as_the_windows_installer_puts_it()
        {
            Subject.Execute(Manual());

            VerifyUpdaterStarted(Times.Once());
        }

        [Test]
        public void should_log_error_when_startup_folder_is_not_writable()
        {
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FolderWritable(It.IsAny<string>())).Returns(false);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(Manual()));

            VerifyDownloaded(Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_switch_to_branch_specified_in_updatepackage()
        {
            _updatePackage.Branch = "fake";

            Subject.Execute(Manual());

            Mocker.GetMock<IConfigFileProvider>()
                  .Verify(v => v.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d => d.ContainsKey("Branch") && (string)d["Branch"] == "fake")), Times.Once());
        }

        [TestCase(UpdateInstallKind.Docker)]
        [TestCase(UpdateInstallKind.External)]
        [TestCase(UpdateInstallKind.NotWritable)]
        [TestCase(UpdateInstallKind.NoPackage)]
        [TestCase(UpdateInstallKind.Development)]
        public void should_only_tell_when_this_install_does_not_update_itself(UpdateInstallKind kind)
        {
            Mocker.GetMock<IUpdateInstallability>().Setup(c => c.Kind(It.IsAny<UpdatePackage>())).Returns(kind);

            Subject.Execute(Manual());

            VerifyDownloaded(Times.Never());
            VerifyUpdaterStarted(Times.Never());
        }

        [Test]
        public void should_not_install_by_itself_when_install_updates_automatically_is_off()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(s => s.UpdateAutomatically).Returns(false);

            Subject.Execute(Scheduled());

            VerifyDownloaded(Times.Never());
        }

        [Test]
        public void should_install_when_asked_although_install_updates_automatically_is_off()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(s => s.UpdateAutomatically).Returns(false);

            Subject.Execute(Manual());

            VerifyUpdaterStarted(Times.Once());
        }

        [Test]
        public void should_install_by_itself_when_nothing_is_running()
        {
            Subject.Execute(Scheduled());

            VerifyUpdaterStarted(Times.Once());
        }

        [Test]
        public void should_wait_while_a_transfer_is_running()
        {
            GivenBusy();

            Subject.Execute(Scheduled());

            VerifyDownloaded(Times.Never());
            Subject.WaitingSince(_updatePackage.SemanticVersion).Should().Be(Now);
        }

        [Test]
        public void should_keep_the_time_it_began_waiting()
        {
            GivenBusy();
            Subject.Execute(Scheduled());

            Subject.Now = () => Now.AddHours(5);
            Subject.Execute(Scheduled());

            Subject.WaitingSince(_updatePackage.SemanticVersion).Should().Be(Now);
            VerifyDownloaded(Times.Never());
        }

        [Test]
        public void should_install_anyway_after_24_hours_of_waiting()
        {
            GivenBusy();
            Subject.Execute(Scheduled());

            Subject.Now = () => Now.Add(InstallUpdateService.MaxWait);
            Subject.Execute(Scheduled());

            VerifyUpdaterStarted(Times.Once());
        }

        [Test]
        public void should_not_wait_when_asked_to_install_now()
        {
            GivenBusy();

            Subject.Execute(Manual());

            VerifyUpdaterStarted(Times.Once());
        }

        [Test]
        public void should_stop_waiting_once_installed()
        {
            GivenBusy();
            Subject.Execute(Scheduled());

            Mocker.GetMock<IUpdateQuietMoment>().Setup(c => c.Busy()).Returns((string)null);
            Subject.Execute(Scheduled());

            VerifyUpdaterStarted(Times.Once());
            Subject.WaitingSince(_updatePackage.SemanticVersion).Should().BeNull();
        }

        [Test]
        public void should_wait_afresh_for_a_different_version()
        {
            GivenBusy();
            Subject.Execute(Scheduled());

            Subject.WaitingSince(SemanticVersion.Parse("10.2.0")).Should().BeNull();
        }

        [Test]
        public void should_not_install_a_new_major_version_by_itself()
        {
            _updatePackage.SemanticVersion = SemanticVersion.Parse("99.0.0");

            Subject.Execute(Scheduled());

            VerifyDownloaded(Times.Never());
        }

        [TearDown]
        public void TearDown()
        {
            ExceptionVerification.IgnoreErrors();
        }
    }
}

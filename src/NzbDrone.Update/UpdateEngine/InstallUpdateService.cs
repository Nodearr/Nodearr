using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;

namespace NzbDrone.Update.UpdateEngine
{
    public interface IInstallUpdateService
    {
        void Start(string installationFolder, int processId);
    }

    public class InstallUpdateService : IInstallUpdateService
    {
        public const string TransportProcessName = "nodearr-transport";

        // How long a new version has to start and answer its API before the old one is put back.
        public static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

        // How long the transport has to leave after Nodearr has, and how long a service manager has to
        // start Nodearr again on Linux and macOS, before the updater acts itself. Shorter in tests.
        public TimeSpan TransportWait { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan ExternalRestartWait { get; set; } = TimeSpan.FromSeconds(10);

        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IDetectApplicationType _detectApplicationType;
        private readonly IDetectExistingVersion _detectExistingVersion;
        private readonly ITerminateNzbDrone _terminateNzbDrone;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IBackupAndRestore _backupAndRestore;
        private readonly IBackupAppData _backupAppData;
        private readonly IStartNzbDrone _startNzbDrone;
        private readonly IStartupCheck _startupCheck;
        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public InstallUpdateService(IDiskProvider diskProvider,
                                    IDiskTransferService diskTransferService,
                                    IDetectApplicationType detectApplicationType,
                                    IDetectExistingVersion detectExistingVersion,
                                    ITerminateNzbDrone terminateNzbDrone,
                                    IAppFolderInfo appFolderInfo,
                                    IBackupAndRestore backupAndRestore,
                                    IBackupAppData backupAppData,
                                    IStartNzbDrone startNzbDrone,
                                    IStartupCheck startupCheck,
                                    IProcessProvider processProvider,
                                    Logger logger)
        {
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _detectApplicationType = detectApplicationType;
            _detectExistingVersion = detectExistingVersion;
            _terminateNzbDrone = terminateNzbDrone;
            _appFolderInfo = appFolderInfo;
            _backupAndRestore = backupAndRestore;
            _backupAppData = backupAppData;
            _startNzbDrone = startNzbDrone;
            _startupCheck = startupCheck;
            _processProvider = processProvider;
            _logger = logger;
        }

        private void Verify(string targetFolder, int processId)
        {
            _logger.Info("Verifying requirements before update...");

            if (string.IsNullOrWhiteSpace(targetFolder))
            {
                throw new ArgumentException("Target folder can not be null or empty");
            }

            if (!_diskProvider.FolderExists(targetFolder))
            {
                throw new DirectoryNotFoundException("Target folder doesn't exist " + targetFolder);
            }

            if (processId < 1)
            {
                throw new ArgumentException("Invalid process ID: " + processId);
            }

            if (!_processProvider.Exists(processId))
            {
                throw new ArgumentException("Process with ID doesn't exist " + processId);
            }

            _logger.Info("Verifying Update Folder");
            if (!_diskProvider.FolderExists(_appFolderInfo.GetUpdatePackageFolder()))
            {
                throw new DirectoryNotFoundException("Update folder doesn't exist " + _appFolderInfo.GetUpdatePackageFolder());
            }
        }

        public void Start(string installationFolder, int processId)
        {
            var oldVersion = _detectExistingVersion.GetExistingVersion(installationFolder);

            _logger.Info("Installation Folder: {0}", installationFolder);
            _logger.Info("Updating Nodearr from version {0} to version {1}", oldVersion, BuildInfo.NodearrVersion);

            Verify(installationFolder, processId);

            if (installationFolder.EndsWith(@"\bin\Nodearr") || installationFolder.EndsWith(@"/bin/Nodearr"))
            {
                installationFolder = installationFolder.GetParentPath();
                _logger.Info("Fixed Installation Folder: {0}", installationFolder);
            }

            var appType = _detectApplicationType.GetAppType();

            _processProvider.FindProcessByName(ProcessProvider.SONARR_CONSOLE_PROCESS_NAME);
            _processProvider.FindProcessByName(ProcessProvider.SONARR_PROCESS_NAME);

            if (OsInfo.IsWindows)
            {
                _terminateNzbDrone.Terminate(processId);

                // The sidecar leaves when Nodearr does, and its program cannot be replaced until it has.
                StopTransport(installationFolder);
            }

            try
            {
                _backupAndRestore.Backup(installationFolder);
                _backupAppData.Backup();

                if (OsInfo.IsWindows)
                {
                    if (_processProvider.Exists(ProcessProvider.SONARR_CONSOLE_PROCESS_NAME) || _processProvider.Exists(ProcessProvider.SONARR_PROCESS_NAME))
                    {
                        _logger.Error("Nodearr was restarted prematurely by external process.");
                        return;
                    }
                }

                try
                {
                    _logger.Info("Copying new files to target folder");

                    if (!OsInfo.IsWindows)
                    {
                        UnlinkReplacedFiles(_appFolderInfo.GetUpdatePackageFolder(), installationFolder);
                    }

                    _diskTransferService.MirrorFolder(_appFolderInfo.GetUpdatePackageFolder(), installationFolder);

                    SetExecutable(installationFolder);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to copy upgrade package to target folder.");
                    _backupAndRestore.Restore(installationFolder);
                    throw;
                }
            }
            finally
            {
                StartAgain(appType, installationFolder, processId);
            }

            if (_startupCheck.Answers(StartupTimeout))
            {
                return;
            }

            _logger.Error("Nodearr {0} did not answer within {1:0} minutes of starting. Putting version {2} back: its program, its database and config.xml.", BuildInfo.NodearrVersion, StartupTimeout.TotalMinutes, oldVersion);

            _terminateNzbDrone.Terminate(processId);
            StopTransport(installationFolder);

            if (!OsInfo.IsWindows)
            {
                UnlinkReplacedFiles(_appFolderInfo.GetUpdateBackUpFolder(), installationFolder);
            }

            _backupAndRestore.Restore(installationFolder);
            SetExecutable(installationFolder);
            _backupAppData.Restore();

            StartAgain(appType, installationFolder, processId);

            if (_startupCheck.Answers(StartupTimeout))
            {
                _logger.Info("Version {0} is running again. The update to {1} was not kept.", oldVersion, BuildInfo.NodearrVersion);
            }
            else
            {
                _logger.Error("Version {0} did not answer either. Start Nodearr by hand and look at its log.", oldVersion);
            }
        }

        private void StartAgain(AppType appType, string installationFolder, int processId)
        {
            if (OsInfo.IsWindows)
            {
                _startNzbDrone.Start(appType, installationFolder);
                return;
            }

            _terminateNzbDrone.Terminate(processId);

            _logger.Info("Waiting for external auto-restart.");
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < ExternalRestartWait)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(1000, ExternalRestartWait.TotalMilliseconds)));

                if (_processProvider.Exists(ProcessProvider.SONARR_PROCESS_NAME))
                {
                    _logger.Info("Nodearr was restarted by external process.");
                    break;
                }
            }

            if (!_processProvider.Exists(ProcessProvider.SONARR_PROCESS_NAME))
            {
                _startNzbDrone.Start(appType, installationFolder);
            }
        }

        // The sidecar exits when its standard input closes, which it does when Nodearr stops. Only the
        // copy in this installation is waited for: another node on the machine keeps its own.
        private void StopTransport(string installationFolder)
        {
            var watch = Stopwatch.StartNew();

            while (true)
            {
                var running = _processProvider.FindProcessByName(TransportProcessName)
                    .Where(p => p.StartPath != null && installationFolder.IsParentPath(p.StartPath))
                    .ToList();

                if (running.Count == 0)
                {
                    return;
                }

                if (watch.Elapsed >= TransportWait)
                {
                    foreach (var process in running)
                    {
                        _logger.Info("Stopping the transport that is still running, {0}", process);
                        _processProvider.Kill(process.Id);
                    }

                    return;
                }

                Thread.Sleep(500);
            }
        }

        // On Linux and macOS a running program, or a library it has loaded, is replaced by removing
        // the old file and writing a new one. Writing over it in place fails or crashes the process.
        // The process keeps the old file until it exits.
        private void UnlinkReplacedFiles(string sourceFolder, string targetFolder)
        {
            if (!_diskProvider.FolderExists(sourceFolder))
            {
                return;
            }

            foreach (var source in _diskProvider.GetFiles(sourceFolder, true))
            {
                var target = Path.Combine(targetFolder, sourceFolder.GetRelativePath(source));

                if (_diskProvider.FileExists(target))
                {
                    _diskProvider.DeleteFile(target);
                }
            }
        }

        private void SetExecutable(string installationFolder)
        {
            if (OsInfo.IsOsx || OsInfo.IsLinux)
            {
                foreach (var name in new[] { "Nodearr", "ffprobe", TransportProcessName, Path.Combine("Nodearr.Update", "Nodearr.Update") })
                {
                    var path = Path.Combine(installationFolder, name);

                    if (_diskProvider.FileExists(path))
                    {
                        _diskProvider.SetFilePermissions(path, "755", null);
                    }
                }
            }
        }
    }
}

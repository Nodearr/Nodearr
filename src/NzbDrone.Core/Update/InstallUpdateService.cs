using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Update.Commands;

namespace NzbDrone.Core.Update
{
    public interface IPendingUpdate
    {
        // When an automatic update first waited for a quiet moment, or null when none is waiting.
        DateTime? WaitingSince(SemanticVersion version);
    }

    // Installs the newer release the last check found (ADR 0024).
    //
    // Install now on System > Updates installs at once. An automatic update waits for a quiet moment,
    // looked for every 5 minutes (UpdateWaiter), and after 24 hours of waiting goes anyway: a transfer
    // cut off by the restart picks up from what had arrived.
    public class InstallUpdateService : IExecute<ApplicationUpdateCommand>, IPendingUpdate
    {
        public static readonly TimeSpan MaxWait = TimeSpan.FromHours(24);

        private const string PendingVersionKey = "updatependingversion";
        private const string PendingSinceKey = "updatependingsince";

        private readonly ICheckUpdateService _checkUpdateService;
        private readonly IUpdateInstallability _installability;
        private readonly IUpdateQuietMoment _quietMoment;
        private readonly IConfigRepository _configRepository;
        private readonly Logger _logger;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IHttpClient _httpClient;
        private readonly IArchiveService _archiveService;
        private readonly IProcessProvider _processProvider;
        private readonly IVerifyUpdates _updateVerifier;
        private readonly IStartupContext _startupContext;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IRuntimeInfo _runtimeInfo;
        private readonly IBackupService _backupService;

        public InstallUpdateService(ICheckUpdateService checkUpdateService,
                                    IUpdateInstallability installability,
                                    IUpdateQuietMoment quietMoment,
                                    IConfigRepository configRepository,
                                    IAppFolderInfo appFolderInfo,
                                    IDiskProvider diskProvider,
                                    IDiskTransferService diskTransferService,
                                    IHttpClient httpClient,
                                    IArchiveService archiveService,
                                    IProcessProvider processProvider,
                                    IVerifyUpdates updateVerifier,
                                    IStartupContext startupContext,
                                    IConfigFileProvider configFileProvider,
                                    IRuntimeInfo runtimeInfo,
                                    IBackupService backupService,
                                    Logger logger)
        {
            _checkUpdateService = checkUpdateService;
            _installability = installability;
            _quietMoment = quietMoment;
            _configRepository = configRepository;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _httpClient = httpClient;
            _archiveService = archiveService;
            _processProvider = processProvider;
            _updateVerifier = updateVerifier;
            _startupContext = startupContext;
            _configFileProvider = configFileProvider;
            _runtimeInfo = runtimeInfo;
            _backupService = backupService;
            _logger = logger;
        }

        // For testing: the time now.
        public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        public DateTime? WaitingSince(SemanticVersion version)
        {
            if (_configRepository.Get(PendingVersionKey)?.Value != version.ToString())
            {
                return null;
            }

            return DateTime.TryParse(_configRepository.Get(PendingSinceKey)?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var since)
                ? since
                : null;
        }

        public void Execute(ApplicationUpdateCommand message)
        {
            var package = GetUpdatePackage(message.Trigger);

            if (package == null)
            {
                return;
            }

            try
            {
                InstallUpdate(package);
                ForgetWaiting();
                _logger.ProgressDebug("Restarting Nodearr to apply updates");
            }
            catch (UpdateFolderNotWritableException ex)
            {
                _logger.Error(ex, "Update process failed");
                throw new CommandFailedException("Startup folder not writable by user '{0}'", ex, Environment.UserName);
            }
            catch (UpdateVerificationFailedException ex)
            {
                _logger.Error(ex, "Update process failed");
                throw new CommandFailedException("Downloaded update package is corrupt", ex);
            }
            catch (UpdateFailedException ex)
            {
                _logger.Error(ex, "Update process failed");
                throw new CommandFailedException(ex);
            }
        }

        private UpdatePackage GetUpdatePackage(CommandTrigger trigger)
        {
            var package = _checkUpdateService.AvailableUpdate();

            if (package == null)
            {
                _logger.ProgressDebug("No update available");
                return null;
            }

            var kind = _installability.Kind(package);

            if (kind is not (UpdateInstallKind.BuiltIn or UpdateInstallKind.Script))
            {
                _logger.ProgressDebug("Nodearr {0} is available, and this install does not update itself ({1})", package.SemanticVersion, kind);
                return null;
            }

            if (trigger == CommandTrigger.Manual)
            {
                return package;
            }

            if (!_configFileProvider.UpdateAutomatically)
            {
                _logger.ProgressDebug("Install updates automatically is off: Nodearr {0} is not installed", package.SemanticVersion);
                return null;
            }

            // As in Sonarr: a new major version is installed only from System > Updates.
            if (package.SemanticVersion.Major > CheckUpdateService.CurrentVersion.Major)
            {
                _logger.ProgressInfo("Nodearr {0} is a new major version. Install it from System > Updates.", package.SemanticVersion);
                return null;
            }

            var now = Now();
            var since = WaitingSince(package.SemanticVersion) ?? StartWaiting(package.SemanticVersion, now);
            var busy = _quietMoment.Busy();

            if (busy != null)
            {
                if (now - since < MaxWait)
                {
                    _logger.ProgressDebug("Nodearr {0} will be installed when nothing is running: {1}", package.SemanticVersion, busy);
                    return null;
                }

                _logger.Info("Installing Nodearr {0} after waiting {1:0} hours for a quiet moment, although {2}", package.SemanticVersion, (now - since).TotalHours, busy);
            }

            return package;
        }

        private DateTime StartWaiting(SemanticVersion version, DateTime now)
        {
            _configRepository.Upsert(PendingVersionKey, version.ToString());
            _configRepository.Upsert(PendingSinceKey, now.ToString("O", CultureInfo.InvariantCulture));

            return now;
        }

        private void ForgetWaiting()
        {
            if (_configRepository.Get(PendingVersionKey) != null)
            {
                _configRepository.Upsert(PendingVersionKey, string.Empty);
                _configRepository.Upsert(PendingSinceKey, string.Empty);
            }
        }

        private void InstallUpdate(UpdatePackage updatePackage)
        {
            EnsureAppDataSafety();

            if (_configFileProvider.UpdateMechanism != UpdateMechanism.Script)
            {
                var startupFolder = _appFolderInfo.StartUpFolder;
                var uiFolder = Path.Combine(startupFolder, "UI");

                if (!_diskProvider.FolderWritable(startupFolder))
                {
                    throw new UpdateFolderNotWritableException("Cannot install update because startup folder '{0}' is not writable by the user '{1}'.", startupFolder, Environment.UserName);
                }

                if (!_diskProvider.FolderWritable(uiFolder))
                {
                    throw new UpdateFolderNotWritableException("Cannot install update because UI folder '{0}' is not writable by the user '{1}'.", uiFolder, Environment.UserName);
                }
            }

            var tempFolder = _appFolderInfo.TempFolder;
            var updateSandboxFolder = _appFolderInfo.GetUpdateSandboxFolder();

            if (_diskProvider.GetTotalSize(tempFolder) < 1.Gigabytes())
            {
                _logger.Warn("Temporary location '{0}' has less than 1 GB free space, Nodearr may not be able to update itself.", tempFolder);
            }

            var packageDestination = Path.Combine(updateSandboxFolder, updatePackage.FileName);

            if (_diskProvider.FolderExists(updateSandboxFolder))
            {
                _logger.Info("Deleting old update files");
                _diskProvider.DeleteFolder(updateSandboxFolder, true);
            }

            _logger.ProgressInfo("Downloading update {0}", updatePackage.SemanticVersion);
            _logger.Debug("Downloading update package from [{0}] to [{1}]", updatePackage.Url, packageDestination);
            Download(updatePackage.Url, packageDestination);

            _logger.ProgressInfo("Verifying update package");

            if (!_updateVerifier.Verify(updatePackage, packageDestination))
            {
                _logger.Error("Update package is invalid");
                throw new UpdateVerificationFailedException("Update file '{0}' is invalid", packageDestination);
            }

            _logger.Info("Update package verified successfully");

            _logger.ProgressInfo("Extracting Update package");
            _archiveService.Extract(packageDestination, updateSandboxFolder);
            _logger.Info("Update package extracted successfully");

            EnsureValidBranch(updatePackage);

            _backupService.Backup(BackupType.Update);

            if (_configFileProvider.UpdateMechanism == UpdateMechanism.Script)
            {
                InstallUpdateWithScript(updateSandboxFolder);
                return;
            }

            _logger.Info("Preparing client");
            _diskTransferService.TransferFolder(_appFolderInfo.GetUpdateClientFolder(), updateSandboxFolder, TransferMode.Move);

            var updateClientExePath = _appFolderInfo.GetUpdateClientExePath();

            if (!_diskProvider.FileExists(updateClientExePath))
            {
                throw new UpdateFailedException("The package has no update client ({0}), so it cannot be installed from here", updateClientExePath);
            }

            // Set executable flag on update app
            if (OsInfo.IsOsx || OsInfo.IsLinux)
            {
                _diskProvider.SetFilePermissions(updateClientExePath, "755", null);
            }

            _logger.Info("Starting update client {0}", updateClientExePath);
            _logger.ProgressInfo("Nodearr will restart shortly.");

            _processProvider.Start(updateClientExePath, GetUpdaterArgs(updateSandboxFolder));
        }

        // Like IHttpClient.DownloadFile, with the plain user agent the manifest is fetched with, no
        // cookies, and time enough for a slow line.
        private void Download(string url, string destination)
        {
            _diskProvider.EnsureFolder(Path.GetDirectoryName(destination));

            var part = destination + ".part";

            try
            {
                using (var stream = new FileStream(part, FileMode.Create, FileAccess.ReadWrite))
                {
                    var request = new HttpRequest(url)
                    {
                        UseSimplifiedUserAgent = true,
                        AllowAutoRedirect = true,
                        StoreRequestCookie = false,
                        StoreResponseCookie = false,
                        RequestTimeout = TimeSpan.FromMinutes(30),
                        ResponseStream = stream
                    };

                    _httpClient.Get(request);
                }

                File.Move(part, destination, true);
            }
            finally
            {
                if (File.Exists(part))
                {
                    File.Delete(part);
                }
            }
        }

        private void EnsureValidBranch(UpdatePackage package)
        {
            var currentBranch = _configFileProvider.Branch;

            if (package.Branch.IsNotNullOrWhiteSpace() && package.Branch != currentBranch)
            {
                try
                {
                    _logger.Info("Branch [{0}] is being redirected to [{1}]]", currentBranch, package.Branch);
                    var config = new Dictionary<string, object>();
                    config["Branch"] = package.Branch;
                    _configFileProvider.SaveConfigDictionary(config);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't change the branch from [{0}] to [{1}].", currentBranch, package.Branch);
                }
            }
        }

        private void InstallUpdateWithScript(string updateSandboxFolder)
        {
            var scriptPath = _configFileProvider.UpdateScriptPath;

            if (scriptPath.IsNullOrWhiteSpace())
            {
                throw new UpdateFailedException("Update Script has not been defined");
            }

            if (!_diskProvider.FileExists(scriptPath, StringComparison.Ordinal))
            {
                throw new UpdateFailedException("Update Script: '{0}' does not exist", scriptPath);
            }

            _logger.Info("Removing Nodearr.Update");
            _diskProvider.DeleteFolder(_appFolderInfo.GetUpdateClientFolder(), true);

            _logger.ProgressInfo("Starting update script: {0}", _configFileProvider.UpdateScriptPath);
            _processProvider.Start(scriptPath, GetUpdaterArgs(updateSandboxFolder));
        }

        private string GetUpdaterArgs(string updateSandboxFolder)
        {
            var processId = _processProvider.GetCurrentProcess().Id.ToString();
            var executingApplication = _runtimeInfo.ExecutingApplication;

            return string.Join(" ", processId, updateSandboxFolder.TrimEnd(Path.DirectorySeparatorChar).WrapInQuotes(), executingApplication.WrapInQuotes(), _startupContext.PreservedArguments);
        }

        private void EnsureAppDataSafety()
        {
            if (_appFolderInfo.StartUpFolder.IsParentPath(_appFolderInfo.AppDataFolder) ||
                _appFolderInfo.StartUpFolder.PathEquals(_appFolderInfo.AppDataFolder))
            {
                throw new UpdateFailedException("Your Nodearr configuration '{0}' is being stored in application folder '{1}' which will cause data lost during the upgrade. Please remove any symlinks or redirects before trying again.", _appFolderInfo.AppDataFolder, _appFolderInfo.StartUpFolder);
            }
        }
    }
}

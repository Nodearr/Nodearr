using System;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Update.UpdateEngine
{
    public interface IBackupAppData
    {
        void Backup();

        // Puts back the database and config.xml as they were before the update, when the new version
        // did not start. It may have changed the database already.
        void Restore();
    }

    public class BackupAppData : IBackupAppData
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public BackupAppData(IAppFolderInfo appFolderInfo,
                             IDiskProvider diskProvider,
                             IDiskTransferService diskTransferService,
                             Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _logger = logger;
        }

        public void Backup()
        {
            _logger.Info("Backing up appdata (database/config)");
            var backupFolderAppData = _appFolderInfo.GetUpdateBackUpAppDataFolder();

            if (_diskProvider.FolderExists(backupFolderAppData))
            {
                _diskProvider.EmptyFolder(backupFolderAppData);
            }
            else
            {
                _diskProvider.CreateFolder(backupFolderAppData);
            }

            try
            {
                _diskTransferService.TransferFile(_appFolderInfo.GetConfigPath(), _appFolderInfo.GetUpdateBackupConfigFile(), TransferMode.Copy);
                _diskTransferService.TransferFile(_appFolderInfo.GetDatabase(), _appFolderInfo.GetUpdateBackupDatabase(), TransferMode.Copy);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't create a data backup");
            }
        }

        public void Restore()
        {
            try
            {
                if (_diskProvider.FileExists(_appFolderInfo.GetUpdateBackupConfigFile()))
                {
                    _diskTransferService.TransferFile(_appFolderInfo.GetUpdateBackupConfigFile(), _appFolderInfo.GetConfigPath(), TransferMode.Copy, true);
                }

                if (_diskProvider.FileExists(_appFolderInfo.GetUpdateBackupDatabase()))
                {
                    // The new version's journal belongs to the database it changed, not to this one.
                    foreach (var journal in new[] { "-wal", "-shm", "-journal" })
                    {
                        var path = _appFolderInfo.GetDatabase() + journal;

                        if (_diskProvider.FileExists(path))
                        {
                            _diskProvider.DeleteFile(path);
                        }
                    }

                    _diskTransferService.TransferFile(_appFolderInfo.GetUpdateBackupDatabase(), _appFolderInfo.GetDatabase(), TransferMode.Copy, true);
                }

                _logger.Info("Put back the database and config.xml from before the update");
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't put back the database and config.xml from before the update. The backup made before it is under System > Backup, and in {0}", _appFolderInfo.GetUpdateBackUpAppDataFolder());
            }
        }
    }
}

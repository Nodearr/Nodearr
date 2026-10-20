using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Backup
{
    // A backup holds the node's key, so a restored node is the node its friends paired with.
    [TestFixture]
    public class BackupServiceFixture : CoreTest<BackupService>
    {
        private string _appData;
        private string _temp;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(Path.GetTempPath(), "nodearr-backup-test", "data");
            _temp = Path.Combine(Path.GetTempPath(), "nodearr-backup-test", "temp");

            Mocker.GetMock<IAppFolderInfo>().SetupGet(f => f.AppDataFolder).Returns(_appData);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(f => f.TempFolder).Returns(_temp);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BackupFolder).Returns("Backups");
            Mocker.GetMock<IMainDatabase>().SetupGet(d => d.DatabaseType).Returns(DatabaseType.SQLite);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderWritable(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles(It.IsAny<string>(), false)).Returns(System.Array.Empty<string>());
        }

        private string BackupTempFolder => Path.Combine(_temp, "nodearr_backup");

        [Test]
        public void a_backup_should_hold_the_node_key()
        {
            var key = NodeKeyFile.GetPath(_appData);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(key)).Returns(true);

            Subject.Backup(BackupType.Manual);

            Mocker.GetMock<IDiskTransferService>()
                .Verify(t => t.TransferFile(key, Path.Combine(BackupTempFolder, NodeKeyFile.FileName), TransferMode.Copy, false), Times.Once());
        }

        [Test]
        public void a_backup_should_be_made_without_a_key_when_there_is_none_yet()
        {
            Subject.Backup(BackupType.Manual);

            Mocker.GetMock<IDiskTransferService>()
                .Verify(t => t.TransferFile(It.IsAny<string>(), Path.Combine(BackupTempFolder, NodeKeyFile.FileName), It.IsAny<TransferMode>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IArchiveService>()
                .Verify(a => a.CreateZip(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Once());
        }

        [Test]
        public void restoring_should_leave_the_key_for_the_transport_to_put_in_place()
        {
            var extracted = Path.Combine(_temp, "nodearr_backup_restore");

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(extracted, false))
                .Returns(new[] { Path.Combine(extracted, "nodearr.db"), Path.Combine(extracted, NodeKeyFile.FileName) });

            Subject.Restore(Path.Combine(_appData, "nodearr_backup_v0.1.0.0_2026.10.15_10.00.00.zip"));

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.MoveFile(Path.Combine(extracted, NodeKeyFile.FileName), NodeKeyFile.GetRestorePath(_appData), true), Times.Once());

            // Never straight over the key a running transport is using.
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.MoveFile(It.IsAny<string>(), NodeKeyFile.GetPath(_appData), It.IsAny<bool>()), Times.Never());
        }

        [TestCase("nodearr_backup_restore.db")]
        [TestCase("nodearr_backup_restore.DB")]
        public void a_database_file_on_its_own_should_be_restored_as_the_database(string name)
        {
            var file = Path.Combine(_temp, name);

            Subject.Restore(file);

            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(file, Mocker.Resolve<IAppFolderInfo>().GetDatabaseRestore(), true), Times.Once());
        }

        // Anything else used to be put where the database is restored from, which would leave the
        // next start without a database.
        [TestCase("nodearr_backup_restore.xml")]
        [TestCase("nodearr_backup_restore.txt")]
        public void anything_else_should_be_refused_and_nothing_moved(string name)
        {
            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore(Path.Combine(_temp, name)));

            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }
    }
}

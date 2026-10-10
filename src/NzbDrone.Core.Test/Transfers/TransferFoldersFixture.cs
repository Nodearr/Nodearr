using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Options;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Transfers
{
    [TestFixture]
    public class TransferFoldersFixture : CoreTest
    {
        private TransferFolders WithFolder(string folder)
        {
            var appFolder = new Mock<IAppFolderInfo>();

            appFolder.SetupGet(a => a.AppDataFolder).Returns(Path.Combine(TempFolder, "data"));

            return new TransferFolders(appFolder.Object, Options.Create(new TransportOptions { DownloadFolder = folder }));
        }

        private static TransferJob Job(string title = "Pingu.S01E01.720p.WEB-DL", string extension = ".mkv", string category = "tv")
        {
            return new TransferJob { DownloadId = "SABnzbd_nzo_abc", Title = title, Extension = extension, Category = category };
        }

        [Test]
        public void transfers_should_go_under_the_data_folder_unless_told_otherwise()
        {
            WithFolder(null).Complete.Should().Be(Path.Combine(TempFolder, "data", "downloads", "complete"));
            WithFolder(Path.Combine(TempFolder, "elsewhere")).Complete.Should().Be(Path.Combine(TempFolder, "elsewhere", "complete"));
        }

        [Test]
        public void a_transfer_should_arrive_in_its_own_folder_and_end_up_under_its_category()
        {
            var folders = WithFolder(TempFolder);

            folders.IncompleteFile(Job()).Should().Be(Path.Combine(TempFolder, "incomplete", "SABnzbd_nzo_abc", "Pingu.S01E01.720p.WEB-DL.mkv"));
            folders.CompleteFolder(Job()).Should().Be(Path.Combine(TempFolder, "complete", "tv", "Pingu.S01E01.720p.WEB-DL"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("mkv")]
        [TestCase(".tar.gz")]
        [TestCase("./../x")]
        public void an_extension_that_is_not_one_should_be_replaced(string extension)
        {
            WithFolder(TempFolder).IncompleteFile(Job(extension: extension)).Should().EndWith("Pingu.S01E01.720p.WEB-DL.mkv");
        }

        [Test]
        public void a_title_should_never_lead_out_of_the_folder()
        {
            var folders = WithFolder(TempFolder);
            var job = Job(title: "..\\..\\Windows/System32: evil?", category: "../up");

            Path.GetFullPath(folders.CompleteFolder(job)).Should().StartWith(Path.Combine(TempFolder, "complete"));
            Path.GetFullPath(folders.IncompleteFile(job)).Should().StartWith(Path.Combine(TempFolder, "incomplete", "SABnzbd_nzo_abc"));
            folders.CompleteFolder(Job(title: "...", category: null)).Should().Be(Path.Combine(TempFolder, "complete", "default", "transfer"));
        }
    }
}

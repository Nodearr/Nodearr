using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Transfers
{
    [TestFixture]
    public class FileSourceServiceFixture : CoreTest<FileSourceService>
    {
        private const string File = "e-1-501";

        private void GivenPeer(PeerState state)
        {
            Mocker.GetMock<IPeerRepository>().Setup(p => p.FindByNodeId("bob")).Returns(new Peer { Id = 2, NodeId = "bob", Name = "Bob", State = state });
        }

        private void GivenOffered(bool offered)
        {
            Mocker.GetMock<ISharingService>().Setup(s => s.IsOffered(It.Is<Peer>(p => p.NodeId == "bob"), File)).Returns(offered);
        }

        private void GivenLocated(LocatedFile located)
        {
            Mocker.GetMock<ICatalogFileLocator>().Setup(l => l.Locate(File)).Returns(located);
        }

        [Test]
        public void a_trusted_node_should_be_given_a_file_it_is_offered()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(true);
            GivenLocated(new LocatedFile { LocalPath = "/library/tv/Pingu/e1.mkv", Readable = true });

            Subject.Resolve("bob", File).Should().Match<FileGrant>(g => g.Path == "/library/tv/Pingu/e1.mkv" && g.Refusal == null);
        }

        [Test]
        public void a_node_nobody_knows_should_be_refused_before_anything_is_looked_up()
        {
            Subject.Resolve("stranger", File).Should().Match<FileGrant>(g => g.Path == null && g.Refusal == FileSourceService.NotAuthorized);

            Mocker.GetMock<ISharingService>().Verify(s => s.IsOffered(It.IsAny<Peer>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ICatalogFileLocator>().Verify(l => l.Locate(It.IsAny<string>()), Times.Never());
            ExceptionVerification.IgnoreWarns();
        }

        [TestCase(PeerState.Pending, FileSourceService.NotAuthorized)]
        [TestCase(PeerState.Revoked, FileSourceService.Revoked)]
        [TestCase(PeerState.Blocked, FileSourceService.Revoked)]
        public void a_node_that_is_not_trusted_now_should_be_refused(PeerState state, string reason)
        {
            GivenPeer(state);
            GivenOffered(true);
            GivenLocated(new LocatedFile { LocalPath = "/library/e1.mkv", Readable = true });

            Subject.Resolve("bob", File).Should().Match<FileGrant>(g => g.Path == null && g.Refusal == reason);
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void a_file_the_node_is_no_longer_offered_should_be_refused_and_never_looked_up()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(false);

            Subject.Resolve("bob", File).Should().Match<FileGrant>(g => g.Path == null && g.Refusal == FileSourceService.NotAuthorized);

            Mocker.GetMock<ICatalogFileLocator>().Verify(l => l.Locate(It.IsAny<string>()), Times.Never());
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void a_file_that_cannot_be_read_here_should_be_reported_as_not_found()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(true);
            GivenLocated(new LocatedFile { LocalPath = "/tv/Pingu/e1.mkv", Readable = false });

            Subject.Resolve("bob", File).Should().Match<FileGrant>(g => g.Path == null && g.Refusal == FileSourceService.NotFound);
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void a_file_that_has_left_the_catalog_should_be_reported_as_not_found()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(true);
            GivenLocated(null);

            Subject.Resolve("bob", File).Refusal.Should().Be(FileSourceService.NotFound);
            ExceptionVerification.IgnoreWarns();
        }

        private void GivenUploads(params FileUploadProgress[] uploads)
        {
            Mocker.GetMock<IFileTransport>().Setup(t => t.ListUploads()).Returns(uploads.ToList());
        }

        private static FileUploadProgress Upload(string id, UploadState state, string file = File, string node = "bob")
        {
            return new FileUploadProgress { Id = id, RemoteNodeId = node, File = file, Path = "/library/" + file + ".mkv", State = state };
        }

        [Test]
        public void unsharing_a_file_should_stop_it_being_sent()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(false);
            GivenUploads(Upload("7", UploadState.Sending));

            Subject.HandleAsync(new SharingChangedEvent());

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload("7", FileSourceService.NotAuthorized), Times.Once());
        }

        [Test]
        public void a_file_being_hashed_before_sending_should_be_stopped_too()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(false);
            GivenUploads(Upload("7", UploadState.Preparing));

            Subject.HandleAsync(new SharingChangedEvent());

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload("7", FileSourceService.NotAuthorized), Times.Once());
        }

        [Test]
        public void a_file_still_shared_should_carry_on()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(true);
            GivenUploads(Upload("7", UploadState.Sending));

            Subject.HandleAsync(new SharingChangedEvent());

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void only_the_upload_no_longer_shared_should_stop()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(true);
            Mocker.GetMock<ISharingService>().Setup(s => s.IsOffered(It.Is<Peer>(p => p.NodeId == "bob"), "m-1-9")).Returns(false);
            GivenUploads(Upload("7", UploadState.Sending), Upload("8", UploadState.Sending, "m-1-9"));

            Subject.HandleAsync(new SharingChangedEvent());

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload("8", FileSourceService.NotAuthorized), Times.Once());
            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload("7", It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_finished_upload_should_be_left_alone()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(false);
            GivenUploads(Upload("7", UploadState.Completed), Upload("8", UploadState.Failed));

            Subject.HandleAsync(new SharingChangedEvent());

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_file_that_left_the_library_should_stop_being_sent()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(false);
            GivenUploads(Upload("7", UploadState.Sending));

            Subject.HandleAsync(new CatalogRefreshedEvent(1));

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload("7", FileSourceService.NotAuthorized), Times.Once());
        }

        [Test]
        public void an_upload_to_a_node_no_longer_trusted_should_be_stopped_as_revoked()
        {
            GivenPeer(PeerState.Revoked);
            GivenUploads(Upload("7", UploadState.Sending));

            Subject.HandleAsync(new SharingChangedEvent());

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload("7", FileSourceService.Revoked), Times.Once());
            Mocker.GetMock<ISharingService>().Verify(s => s.IsOffered(It.IsAny<Peer>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_transport_that_is_not_running_should_be_left_alone()
        {
            Mocker.GetMock<IFileTransport>().Setup(t => t.ListUploads()).Throws(new TransportException(TransportFailure.Unavailable, "not running"));

            Subject.HandleAsync(new SharingChangedEvent());

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_failure_to_stop_one_upload_should_not_spare_the_next()
        {
            GivenPeer(PeerState.Trusted);
            GivenOffered(false);
            GivenUploads(Upload("7", UploadState.Sending), Upload("8", UploadState.Sending));
            Mocker.GetMock<IFileTransport>().Setup(t => t.StopUpload("7", It.IsAny<string>())).Throws(new TransportException(TransportFailure.Unavailable, "not answering"));

            Subject.HandleAsync(new SharingChangedEvent());

            Mocker.GetMock<IFileTransport>().Verify(t => t.StopUpload("8", FileSourceService.NotAuthorized), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}

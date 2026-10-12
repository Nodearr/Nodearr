using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Transfers
{
    [TestFixture]
    public class TransferServiceFixture : DbTest<TransferService, TransferJob>
    {
        private const string Title = "Pingu.S01E01.720p.WEB-DL";

        private Peer _alice;
        private ITransferFolders _folders;
        private List<FileTransferProgress> _progress;
        private List<FileTransferRequest> _started;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<ITransferRepository>(Mocker.Resolve<TransferRepository>());

            Mocker.GetMock<INodeSettingsProvider>().SetupGet(s => s.DownloadFolder).Returns(TempFolder);
            _folders = new TransferFolders(Mocker.GetMock<INodeSettingsProvider>().Object);
            Mocker.SetConstant(_folders);

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted, IpAddresses = "192.0.2.1:4000", Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });

            GivenCandidate("candidate-1", "e-1-501", Title);

            _progress = new List<FileTransferProgress>();
            _started = new List<FileTransferRequest>();

            Mocker.GetMock<IFileTransport>().Setup(t => t.List()).Returns(() => _progress.ToList());
            Mocker.GetMock<IFileTransport>().Setup(t => t.Start(It.IsAny<FileTransferRequest>())).Callback<FileTransferRequest>(_started.Add);
        }

        [TearDown]
        public void IgnoreExpectedWarnings()
        {
            // A failed transfer is logged as a warning, and many of these tests fail one on purpose.
            ExceptionVerification.IgnoreWarns();
        }

        private void GivenCandidate(string candidateId, string remoteId, string title)
        {
            Mocker.GetMock<ICandidateService>()
                  .Setup(c => c.Find(candidateId))
                  .Returns(new PeerCatalogItem { PeerId = _alice.Id, CandidateId = candidateId, RemoteId = remoteId, MediaKind = MediaKind.Tv, ReleaseTitle = title, Extension = ".mkv", Size = 1000 });
        }

        // The transport reports on a transfer the way the sidecar does. It also writes the file,
        // because a transfer that completes has left its file under incomplete.
        private void GivenTransport(TransferJob job, FileTransferState state, long bytesDone = 0, string failure = null)
        {
            _progress.RemoveAll(p => p.Id == job.DownloadId);
            _progress.Add(new FileTransferProgress { Id = job.DownloadId, State = state, BytesDone = bytesDone, Size = 1000, Hash = "hash-1", FailureKind = failure, FailureMessage = failure == null ? null : "detail" });

            if (state is FileTransferState.Receiving or FileTransferState.Completed)
            {
                Directory.CreateDirectory(_folders.IncompleteFolder(job));
                File.WriteAllText(_folders.IncompleteFile(job), "bytes");
            }
        }

        private TransferJob Stored(TransferJob job) => Subject.Find(job.DownloadId);

        private TransferJob GivenDownloading()
        {
            var job = Subject.Add("candidate-1", "tv");

            Subject.Tick();

            return Stored(job);
        }

        [Test]
        public void a_ticket_should_become_a_queued_transfer()
        {
            var job = Subject.Add("candidate-1", "tv");

            job.Should().Match<TransferJob>(j => j.State == TransferState.Queued && j.Title == Title && j.RemoteId == "e-1-501" && j.PeerNodeId == "alice" && j.Category == "tv" && j.Size == 1000);
            job.DownloadId.Should().StartWith("SABnzbd_nzo_");
        }

        [Test]
        public void the_same_ticket_twice_should_be_one_transfer()
        {
            var first = Subject.Add("candidate-1", "tv");
            var second = Subject.Add("candidate-1", "tv");

            second.DownloadId.Should().Be(first.DownloadId);
            Subject.All().Should().HaveCount(1);
        }

        [Test]
        public void a_ticket_for_something_nobody_offers_should_be_refused()
        {
            Subject.Add("gone", "tv").Should().BeNull();
            Subject.All().Should().BeEmpty();
        }

        [Test]
        public void a_queued_transfer_should_be_started_with_the_peers_name_for_the_file()
        {
            var job = GivenDownloading();

            job.State.Should().Be(TransferState.Downloading);
            job.Attempts.Should().Be(1);
            _started.Should().ContainSingle().Which.Should().Match<FileTransferRequest>(r =>
                r.Id == job.DownloadId && r.File == "e-1-501" && r.Peer.NodeId == "alice" && r.ExpectedHash == null &&
                r.Destination == _folders.IncompleteFile(job));
            _started[0].Destination.Should().StartWith(Path.Combine(TempFolder, "incomplete")).And.EndWith(Title + ".mkv");
        }

        [Test]
        public void only_so_many_transfers_should_run_at_once()
        {
            for (var i = 0; i < TransferService.MaxConcurrent + 2; i++)
            {
                GivenCandidate("c" + i, "e-1-" + i, "Title." + i);
                Subject.Add("c" + i, "tv");
            }

            Subject.Tick();

            Subject.All().Count(j => j.State == TransferState.Downloading).Should().Be(TransferService.MaxConcurrent);
            Subject.All().Count(j => j.State == TransferState.Queued).Should().Be(2);
        }

        [Test]
        public void progress_should_be_followed()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();

            Stored(job).Should().Match<TransferJob>(j => j.BytesDone == 400 && j.Hash == "hash-1" && j.State == TransferState.Downloading);
        }

        [Test]
        public void a_verified_file_should_appear_under_complete_in_one_step()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Completed, 1000);
            Subject.Tick();

            var done = Stored(job);
            var expected = Path.Combine(TempFolder, "complete", "tv", Title);

            done.Should().Match<TransferJob>(j => j.State == TransferState.Completed && j.OutputPath == expected && j.BytesDone == 1000 && j.Ended != null && j.Failure == TransferFailure.None);
            File.Exists(Path.Combine(expected, Title + ".mkv")).Should().BeTrue();
            Directory.Exists(_folders.IncompleteFolder(job)).Should().BeFalse();
            Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(job.DownloadId), Times.Once());
        }

        [Test]
        public void nothing_should_be_under_complete_while_a_transfer_is_arriving()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();

            Directory.Exists(Path.Combine(TempFolder, "complete")).Should().BeFalse();
        }

        [TestCase("NoRoute", TransferFailure.PeerUnreachable)]
        [TestCase("DirectPathLost", TransferFailure.DirectPathLost)]
        [TestCase("NoDirectRoute", TransferFailure.NoDirectRoute)]
        [TestCase("ConnectionLost", TransferFailure.ConnectionLost)]
        public void a_failure_that_may_pass_should_be_tried_again_from_where_it_stopped(string kind, TransferFailure expected)
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();
            GivenTransport(job, FileTransferState.Failed, 400, kind);
            Subject.Tick();

            var waiting = Stored(job);

            waiting.Should().Match<TransferJob>(j => j.State == TransferState.Queued && j.Failure == expected && j.NextAttempt > DateTime.UtcNow && j.Hash == "hash-1");
            File.Exists(_folders.IncompleteFile(job)).Should().BeTrue();

            // Not before its time.
            Subject.Tick();
            _started.Should().HaveCount(1);

            waiting.NextAttempt = DateTime.UtcNow.AddSeconds(-1);
            Db.Update(waiting);
            _progress.Clear();
            Subject.Tick();

            _started.Should().HaveCount(2);
            _started[1].ExpectedHash.Should().Be("hash-1");
            Stored(job).Attempts.Should().Be(2);
        }

        [Test]
        public void a_failure_that_keeps_happening_should_end_the_transfer()
        {
            var job = GivenDownloading();

            for (var attempt = 1; attempt <= TransferService.MaxAttempts; attempt++)
            {
                GivenTransport(job, FileTransferState.Failed, 0, "NoDirectRoute");
                Subject.Tick();

                var current = Stored(job);

                if (current.State == TransferState.Queued)
                {
                    current.NextAttempt = DateTime.UtcNow.AddSeconds(-1);
                    Db.Update(current);
                    _progress.Clear();
                    Subject.Tick();
                }
            }

            Stored(job).Should().Match<TransferJob>(j => j.State == TransferState.Failed && j.Failure == TransferFailure.NoDirectRoute && j.Attempts == TransferService.MaxAttempts);
            Stored(job).FailureMessage.Should().StartWith("Nodearr transfer failed: NoDirectRoute.").And.Contain("never sends files through a relay");
        }

        [TestCase("NotAuthorized", TransferFailure.NotAuthorized)]
        [TestCase("Revoked", TransferFailure.Revoked)]
        [TestCase("Blocked", TransferFailure.Blocked)]
        [TestCase("NotFound", TransferFailure.NotFound)]
        [TestCase("VerificationFailed", TransferFailure.VerificationFailed)]
        [TestCase("DiskError", TransferFailure.DiskError)]
        public void a_failure_that_will_not_pass_should_end_the_transfer_at_once(string kind, TransferFailure expected)
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();
            GivenTransport(job, FileTransferState.Failed, 400, kind);
            Subject.Tick();

            Stored(job).Should().Match<TransferJob>(j => j.State == TransferState.Failed && j.Failure == expected && j.Ended != null);
            Stored(job).FailureMessage.Should().StartWith($"Nodearr transfer failed: {expected}.");
            Directory.Exists(_folders.IncompleteFolder(job)).Should().BeFalse();
            _started.Should().HaveCount(1);
        }

        [Test]
        public void a_transfer_the_transport_has_forgotten_should_be_asked_for_again_without_costing_an_attempt()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();

            // Nodearr, or the transport, was restarted.
            _progress.Clear();
            Subject.Tick();

            _started.Should().HaveCount(2);
            _started[1].ExpectedHash.Should().Be("hash-1");
            Stored(job).Should().Match<TransferJob>(j => j.State == TransferState.Downloading && j.Attempts == 1 && j.BytesDone == 400);
        }

        [Test]
        public void a_transfer_that_is_reconnecting_should_keep_showing_what_it_holds()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();
            GivenTransport(job, FileTransferState.Connecting);
            Subject.Tick();

            Stored(job).BytesDone.Should().Be(400);
        }

        [Test]
        public void everything_should_wait_while_the_transport_is_not_running()
        {
            var job = Subject.Add("candidate-1", "tv");

            Mocker.GetMock<IFileTransport>().Setup(t => t.List()).Throws(new TransportException(TransportFailure.Unavailable, "not running"));
            Subject.Tick();

            Stored(job).State.Should().Be(TransferState.Queued);
            _started.Should().BeEmpty();
        }

        [Test]
        public void removing_a_transfer_that_is_under_way_should_stop_it_and_leave_nothing()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();

            Subject.Remove(job.DownloadId, true).Should().BeTrue();

            Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(job.DownloadId), Times.Once());
            Directory.Exists(_folders.IncompleteFolder(job)).Should().BeFalse();
            Subject.All().Should().BeEmpty();
            Subject.Remove(job.DownloadId, true).Should().BeFalse();
        }

        [Test]
        public void removing_a_finished_transfer_should_remove_its_file_only_when_asked()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Completed, 1000);
            Subject.Tick();

            var output = Stored(job).OutputPath;

            Subject.Remove(job.DownloadId, false);
            Directory.Exists(output).Should().BeTrue();

            var again = Subject.Add("candidate-1", "tv");

            Subject.Tick();
            GivenTransport(again, FileTransferState.Completed, 1000);
            Subject.Tick();
            Subject.Remove(again.DownloadId, true);

            Directory.Exists(output).Should().BeFalse();
        }

        [Test]
        public void a_failed_transfer_should_be_retried_as_a_new_one()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Failed, 0, "NotFound");
            Subject.Tick();

            var again = Subject.Retry(job.DownloadId);

            again.DownloadId.Should().NotBe(job.DownloadId);
            again.State.Should().Be(TransferState.Queued);
            Subject.All().Should().ContainSingle();
            Subject.Retry(again.DownloadId).Should().BeNull();
        }

        [TestCase(PeerState.Revoked, TransferFailure.Revoked)]
        [TestCase(PeerState.Blocked, TransferFailure.Blocked)]
        public void the_end_of_trust_should_end_every_transfer_with_that_node_both_ways(PeerState state, TransferFailure expected)
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();

            _alice.State = state;
            Subject.Handle(new PeerStateChangedEvent(_alice, PeerState.Trusted));

            Stored(job).Should().Match<TransferJob>(j => j.State == TransferState.Failed && j.Failure == expected);
            Directory.Exists(_folders.IncompleteFolder(job)).Should().BeFalse();
            Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(job.DownloadId), Times.Once());

            // And nothing more is sent to it.
            Mocker.GetMock<IFileTransport>().Verify(t => t.Disconnect("alice"), Times.Once());
        }

        [Test]
        public void a_transfer_should_not_start_once_its_node_is_no_longer_trusted()
        {
            var job = Subject.Add("candidate-1", "tv");

            _alice.State = PeerState.Revoked;
            Mocker.Resolve<IPeerRepository>().Update(_alice);
            Subject.Tick();

            Stored(job).Should().Match<TransferJob>(j => j.State == TransferState.Failed && j.Failure == TransferFailure.Revoked);
            _started.Should().BeEmpty();
        }

        [Test]
        public void every_change_should_be_announced()
        {
            Subject.Add("candidate-1", "tv");
            Subject.Tick();

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<TransfersChangedEvent>()), Times.Exactly(2));
        }
    }
}

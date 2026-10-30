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
        private int _maxDownloads;
        private bool _transportReportsStarted;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<ITransferRepository>(Mocker.Resolve<TransferRepository>());

            Mocker.GetMock<INodeSettingsProvider>().SetupGet(s => s.DownloadFolder).Returns(TempFolder);
            _maxDownloads = NodeSettingsDefaults.MaxDownloads;
            Mocker.GetMock<INodeSettingsProvider>().Setup(s => s.Get()).Returns(() => new NodeSettings { MaxDownloads = _maxDownloads });
            _folders = new TransferFolders(Mocker.GetMock<INodeSettingsProvider>().Object);
            Mocker.SetConstant(_folders);

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted, IpAddresses = "192.0.2.1:4000", Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });

            GivenCandidate("candidate-1", "e-1-501", Title);

            // No other node has the file unless a test says so.
            Mocker.GetMock<ICandidateService>().Setup(c => c.Copies(It.IsAny<string>())).Returns(new List<PeerCatalogItem>());

            _progress = new List<FileTransferProgress>();
            _started = new List<FileTransferRequest>();

            Mocker.GetMock<IFileTransport>().Setup(t => t.List()).Returns(() => _progress.ToList());
            _transportReportsStarted = false;
            Mocker.GetMock<IFileTransport>().Setup(t => t.Start(It.IsAny<FileTransferRequest>())).Callback<FileTransferRequest>(r =>
            {
                _started.Add(r);

                // Only where a test says so: most leave the transport silent, as after a restart.
                if (_transportReportsStarted)
                {
                    _progress.Add(new FileTransferProgress { Id = r.Id, State = FileTransferState.Connecting, Size = 1000 });
                }
            });
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

        private void GivenAiredCandidate()
        {
            Mocker.GetMock<ICandidateService>()
                  .Setup(c => c.Find("candidate-daily"))
                  .Returns(new PeerCatalogItem
                  {
                      PeerId = _alice.Id, CandidateId = "candidate-daily", RemoteId = "e-1-900", MediaKind = MediaKind.Tv, Title = "The Daily Show",
                      SeasonNumber = 29, EpisodeNumbers = "45", AirDates = "2024-05-17", Quality = "WEBDL-720p",
                      ReleaseTitle = "The.Daily.Show.S29E45.720p.WEB-DL", Extension = ".mkv", Size = 1000
                  });
        }

        [Test]
        public void a_ticket_found_by_day_should_be_fetched_under_a_title_with_that_day()
        {
            GivenAiredCandidate();

            var job = Subject.Add("candidate-daily", "tv", "2024-05-17");

            job.Title.Should().Be("The.Daily.Show.2024.05.17.720p.WEB-DL");
            job.AirDate.Should().Be("2024-05-17");
            _folders.IncompleteFile(job).Should().EndWith("The.Daily.Show.2024.05.17.720p.WEB-DL.mkv");
        }

        [TestCase(null)]
        [TestCase("2024-05-18")]
        [TestCase("..\\..\\somewhere")]
        public void a_ticket_with_no_day_or_one_the_file_does_not_have_should_keep_the_title_by_number(string airDate)
        {
            GivenAiredCandidate();

            var job = Subject.Add("candidate-daily", "tv", airDate);

            job.Title.Should().Be("The.Daily.Show.S29E45.720p.WEB-DL");
            job.AirDate.Should().BeNull();
        }

        [Test]
        public void trying_again_should_keep_the_day_a_transfer_was_asked_for_by()
        {
            GivenAiredCandidate();

            var job = Subject.Add("candidate-daily", "tv", "2024-05-17");

            Subject.Tick();
            GivenTransport(job, FileTransferState.Failed, 0, "NotFound");
            Subject.Tick();

            var again = Subject.Retry(job.DownloadId);

            again.Title.Should().Be("The.Daily.Show.2024.05.17.720p.WEB-DL");
            again.AirDate.Should().Be("2024-05-17");
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

        private List<TransferJob> GivenWaiting(int count)
        {
            _transportReportsStarted = true;
            var jobs = new List<TransferJob>();

            for (var i = 0; i < count; i++)
            {
                GivenCandidate("c" + i, "e-1-" + i, "Title." + i);
                jobs.Add(Subject.Add("c" + i, "tv"));
            }

            return jobs;
        }

        private void Finish(TransferJob job)
        {
            GivenTransport(job, FileTransferState.Completed, 1000);
        }

        [Test]
        public void only_so_many_transfers_should_run_at_once()
        {
            GivenWaiting(NodeSettingsDefaults.MaxDownloads + 2);

            Subject.Tick();

            Subject.All().Count(j => j.State == TransferState.Downloading).Should().Be(NodeSettingsDefaults.MaxDownloads);
            Subject.All().Count(j => j.State == TransferState.Queued).Should().Be(2);
        }

        [Test]
        public void a_download_the_other_node_has_queued_should_show_its_place_until_it_arrives()
        {
            var job = GivenDownloading();

            _progress.Add(new FileTransferProgress { Id = job.DownloadId, State = FileTransferState.Waiting, QueuePosition = 2 });
            Subject.Tick();

            Stored(job).Should().Match<TransferJob>(j => j.State == TransferState.Downloading && j.WaitingPosition == 2 && j.IsWaitingForPeer && j.BytesDone == 0);

            GivenTransport(job, FileTransferState.Receiving, 300);
            Subject.Tick();

            Stored(job).Should().Match<TransferJob>(j => j.WaitingPosition == null && !j.IsWaitingForPeer && j.BytesDone == 300);
        }

        [Test]
        public void a_download_waiting_at_the_other_node_should_still_hold_its_slot()
        {
            _maxDownloads = 1;
            var jobs = GivenWaiting(2);
            Subject.Tick();

            _progress.RemoveAll(p => p.Id == jobs[0].DownloadId);
            _progress.Add(new FileTransferProgress { Id = jobs[0].DownloadId, State = FileTransferState.Waiting, QueuePosition = 1 });
            Subject.Tick();

            Stored(jobs[0]).IsWaitingForPeer.Should().BeTrue();
            Stored(jobs[1]).State.Should().Be(TransferState.Queued);
        }

        [Test]
        public void transfers_should_wait_their_turn_in_the_order_they_were_asked_for()
        {
            _maxDownloads = 1;
            var jobs = GivenWaiting(3);

            Subject.Tick();
            Stored(jobs[0]).State.Should().Be(TransferState.Downloading);
            Stored(jobs[1]).State.Should().Be(TransferState.Queued);
            Stored(jobs[2]).State.Should().Be(TransferState.Queued);

            Finish(jobs[0]);
            Subject.Tick();
            Subject.Tick();

            Stored(jobs[0]).State.Should().Be(TransferState.Completed);
            Stored(jobs[1]).State.Should().Be(TransferState.Downloading);
            Stored(jobs[2]).State.Should().Be(TransferState.Queued);
            _started.Select(r => r.Id).Should().Equal(jobs[0].DownloadId, jobs[1].DownloadId);
        }

        [Test]
        public void no_limit_should_start_everything_at_once()
        {
            _maxDownloads = 0;
            GivenWaiting(8);

            Subject.Tick();

            Subject.All().Should().OnlyContain(j => j.State == TransferState.Downloading);
        }

        [Test]
        public void a_new_limit_should_apply_at_the_next_tick_without_stopping_what_runs()
        {
            _maxDownloads = 1;
            var jobs = GivenWaiting(4);
            Subject.Tick();

            _maxDownloads = 3;
            Subject.Tick();
            Subject.All().Count(j => j.State == TransferState.Downloading).Should().Be(3);

            _maxDownloads = 1;
            Subject.Tick();
            Subject.All().Count(j => j.State == TransferState.Downloading).Should().Be(3);
            Stored(jobs[3]).State.Should().Be(TransferState.Queued);
        }

        [Test]
        public void a_queued_transfer_should_survive_a_restart_and_start_when_its_turn_comes()
        {
            _maxDownloads = 1;
            var jobs = GivenWaiting(2);
            Subject.Tick();

            // A new service reads the same stored transfers, as Nodearr does when it starts again.
            var restarted = Mocker.Resolve<TransferService>();
            restarted.Find(jobs[1].DownloadId).State.Should().Be(TransferState.Queued);

            Finish(jobs[0]);
            restarted.Tick();
            restarted.Tick();

            restarted.Find(jobs[1].DownloadId).State.Should().Be(TransferState.Downloading);
        }

        [Test]
        public void removing_a_queued_transfer_should_not_ask_the_transport_anything()
        {
            _maxDownloads = 1;
            var jobs = GivenWaiting(2);
            Subject.Tick();

            Subject.Remove(jobs[1].DownloadId, true).Should().BeTrue();

            Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(jobs[1].DownloadId), Times.Never());
            Subject.Find(jobs[1].DownloadId).Should().BeNull();
            _started.Select(r => r.Id).Should().Equal(jobs[0].DownloadId);
        }

        [Test]
        public void trying_a_failed_transfer_again_should_wait_behind_those_already_waiting()
        {
            _maxDownloads = 1;
            var jobs = GivenWaiting(3);
            Subject.Tick();
            GivenTransport(jobs[0], FileTransferState.Failed, 0, "NotFound");
            Subject.Tick();
            Stored(jobs[0]).State.Should().Be(TransferState.Failed);

            var again = Subject.Retry(jobs[0].DownloadId);
            Subject.Tick();

            Stored(jobs[1]).State.Should().Be(TransferState.Downloading);
            Subject.Find(again.DownloadId).State.Should().Be(TransferState.Queued);
            Stored(jobs[2]).State.Should().Be(TransferState.Queued);
            Subject.All().Where(j => j.State == TransferState.Queued).OrderBy(j => j.Added).Select(j => j.DownloadId).Should().Equal(jobs[2].DownloadId, again.DownloadId);
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

        [Test]
        public void a_local_address_change_should_reopen_a_stalled_transfer_from_its_saved_bytes()
        {
            var job = GivenDownloading();
            GivenTransport(job, FileTransferState.Receiving, 620);

            Subject.HandleAsync(new PeerTransportAddressChangedEvent());

            var waiting = Stored(job);
            waiting.State.Should().Be(TransferState.Queued);
            waiting.BytesDone.Should().Be(620);
            waiting.Hash.Should().Be("hash-1");
            waiting.DownloadId.Should().Be(job.DownloadId);
            waiting.Attempts.Should().Be(0);
            waiting.NextAttempt.Should().BeAfter(DateTime.UtcNow);
            File.Exists(_folders.IncompleteFile(job)).Should().BeTrue();
            Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(job.TransportId), Times.Once());

            // Another announcement while queued neither spends an attempt nor postpones it.
            Subject.HandleAsync(new PeerTransportAddressChangedEvent());
            Stored(job).NextAttempt.Should().Be(waiting.NextAttempt);
            Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(job.TransportId), Times.Once());

            waiting.NextAttempt = DateTime.UtcNow.AddSeconds(-1);
            Db.Update(waiting);
            _progress.Clear();
            Subject.Tick();

            _started.Should().HaveCount(2);
            _started[1].ExpectedHash.Should().Be("hash-1");
            _started[1].Destination.Should().Be(_started[0].Destination);
            _started[1].Id.Should().Be(_started[0].Id);
            Stored(job).Attempts.Should().Be(1);
        }

        [Test]
        public void a_friends_address_change_should_reopen_only_that_friends_active_downloads()
        {
            var job = GivenDownloading();
            GivenTransport(job, FileTransferState.Receiving, 620);
            var peers = Mocker.Resolve<IPeerRepository>();
            var alice = peers.Find(_alice.Id);
            alice.IpAddresses = "192.0.2.2:5000";
            peers.Update(alice);

            Subject.HandleAsync(new PeerAddressChangedEvent(_alice.Id + 100));
            Stored(job).State.Should().Be(TransferState.Downloading);
            Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(It.IsAny<string>()), Times.Never());

            Subject.HandleAsync(new PeerAddressChangedEvent(_alice.Id));
            var waiting = Stored(job);
            waiting.State.Should().Be(TransferState.Queued);
            waiting.NextAttempt = DateTime.UtcNow.AddSeconds(-1);
            Db.Update(waiting);
            _progress.Clear();
            Subject.Tick();

            _started[1].Peer.IpAddresses.Should().Equal("192.0.2.2:5000");
            _started[1].ExpectedHash.Should().Be("hash-1");
        }

        [Test]
        public void a_local_address_change_should_reopen_every_running_download_and_leave_queued_work_alone()
        {
            var first = GivenDownloading();
            GivenTransport(first, FileTransferState.Receiving, 620);
            GivenCandidate("candidate-2", "file-2", "Second.File");
            var second = Subject.Add("candidate-2", "tv");
            Subject.Tick();
            GivenTransport(second, FileTransferState.Receiving, 300);
            GivenCandidate("candidate-3", "file-3", "Third.File");
            var queued = Subject.Add("candidate-3", "tv");

            Subject.HandleAsync(new PeerTransportAddressChangedEvent());

            foreach (var job in new[] { first, second })
            {
                Stored(job).State.Should().Be(TransferState.Queued);
                Stored(job).Attempts.Should().Be(0);
                Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(job.TransportId), Times.Once());
            }

            Stored(queued).NextAttempt.Should().BeNull();
            Mocker.GetMock<IFileTransport>().Verify(t => t.Cancel(queued.TransportId), Times.Never());
        }

        [Test]
        public void a_network_change_with_unavailable_progress_should_keep_the_last_known_hash_and_partial_file()
        {
            var job = GivenDownloading();
            GivenTransport(job, FileTransferState.Receiving, 620);
            Subject.Tick();
            Mocker.GetMock<IFileTransport>().Setup(t => t.List()).Throws(new TransportException(TransportFailure.Unavailable, "not responding"));

            Subject.HandleAsync(new PeerTransportAddressChangedEvent());

            Stored(job).State.Should().Be(TransferState.Queued);
            Stored(job).Hash.Should().Be("hash-1");
            Stored(job).BytesDone.Should().Be(620);
            File.Exists(_folders.IncompleteFile(job)).Should().BeTrue();
        }

        [Test]
        public void a_network_change_should_not_reopen_a_transfer_that_has_already_completed()
        {
            var job = GivenDownloading();
            GivenTransport(job, FileTransferState.Completed, 1000);

            Subject.HandleAsync(new PeerTransportAddressChangedEvent());

            Stored(job).State.Should().Be(TransferState.Completed);
            _started.Should().HaveCount(1);
            Directory.Exists(Stored(job).OutputPath).Should().BeTrue();
        }

        [Test]
        public void a_network_change_should_keep_the_normal_failure_policy_for_an_already_failed_attempt()
        {
            var job = GivenDownloading();
            GivenTransport(job, FileTransferState.Failed, 620, "NotAuthorized");

            Subject.HandleAsync(new PeerTransportAddressChangedEvent());

            Stored(job).State.Should().Be(TransferState.Failed);
            Stored(job).Failure.Should().Be(TransferFailure.NotAuthorized);
        }

        [TestCase(FileTransferState.Connecting)]
        [TestCase(FileTransferState.Waiting)]
        public void a_network_change_should_reopen_an_attempt_waiting_on_the_old_connection(FileTransferState state)
        {
            var job = GivenDownloading();
            GivenTransport(job, state, 0);

            Subject.HandleAsync(new PeerTransportAddressChangedEvent());

            Stored(job).State.Should().Be(TransferState.Queued);
            Stored(job).WaitingPosition.Should().BeNull();
            Stored(job).Attempts.Should().Be(0);
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

        [Test]
        public void a_file_that_did_not_match_its_hash_should_be_tried_again_from_nothing()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();
            GivenTransport(job, FileTransferState.Failed, 1000, "VerificationFailed");
            Subject.Tick();

            var waiting = Stored(job);

            waiting.Should().Match<TransferJob>(j => j.State == TransferState.Queued && j.Failure == TransferFailure.VerificationFailed && j.Hash == null && j.BytesDone == 0);
            Directory.Exists(_folders.IncompleteFolder(job)).Should().BeFalse();

            waiting.NextAttempt = DateTime.UtcNow.AddSeconds(-1);
            Db.Update(waiting);
            _progress.Clear();
            Subject.Tick();

            // Nothing is resumed: the other node is not asked to carry on from a hash that was wrong.
            _started.Should().HaveCount(2);
            _started[1].ExpectedHash.Should().BeNull();
            Stored(job).Attempts.Should().Be(2);
        }

        [Test]
        public void a_file_that_never_matches_its_hash_should_end_the_transfer()
        {
            var job = GivenDownloading();

            for (var attempt = 1; attempt <= TransferService.MaxAttempts; attempt++)
            {
                GivenTransport(job, FileTransferState.Failed, 1000, "VerificationFailed");
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

            Stored(job).Should().Match<TransferJob>(j => j.State == TransferState.Failed && j.Failure == TransferFailure.VerificationFailed && j.Attempts == TransferService.MaxAttempts);
            Stored(job).FailureMessage.Should().StartWith("Nodearr transfer failed: VerificationFailed.");
            Directory.Exists(_folders.IncompleteFolder(job)).Should().BeFalse();
        }

        [TestCase("NoDirectRoute")]
        [TestCase("DirectPathLost")]
        public void a_transfer_with_no_direct_route_should_say_what_is_known_about_the_route(string kind)
        {
            Mocker.GetMock<IRouteDiagnosis>()
                  .Setup(r => r.Describe(It.Is<Peer>(p => p.NodeId == "alice")))
                  .Returns("A connection check just before this reached Alice only through a relay.");

            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Failed, 0, kind);
            Subject.Tick();

            // Said while it waits to try again, and it would be said again if it failed for good.
            Stored(job).State.Should().Be(TransferState.Queued);
            Stored(job).FailureMessage.Should().StartWith($"Nodearr transfer failed: {kind}.").And.EndWith("reached Alice only through a relay.");
        }

        [Test]
        public void a_failure_that_is_not_about_the_route_should_not_talk_about_the_route()
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Failed, 0, "VerificationFailed");
            Subject.Tick();

            Mocker.GetMock<IRouteDiagnosis>().Verify(r => r.Describe(It.IsAny<Peer>()), Times.Never());
        }

        [Test]
        public void each_attempt_that_ends_should_be_announced_with_how_it_ended()
        {
            var ended = new List<TransferFailure>();

            Mocker.GetMock<IEventAggregator>()
                  .Setup(e => e.PublishEvent(It.IsAny<TransferAttemptEndedEvent>()))
                  .Callback<TransferAttemptEndedEvent>(e =>
                  {
                      e.PeerId.Should().Be(_alice.Id);
                      ended.Add(e.Failure);
                  });

            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();
            ended.Should().BeEmpty();

            GivenTransport(job, FileTransferState.Failed, 400, "NoDirectRoute");
            Subject.Tick();

            var waiting = Stored(job);

            waiting.NextAttempt = DateTime.UtcNow.AddSeconds(-1);
            Db.Update(waiting);
            _progress.Clear();
            Subject.Tick();

            GivenTransport(job, FileTransferState.Completed, 1000);
            Subject.Tick();

            ended.Should().Equal(TransferFailure.NoDirectRoute, TransferFailure.None);
        }

        [TestCase("NotAuthorized", TransferFailure.NotAuthorized)]
        [TestCase("Revoked", TransferFailure.Revoked)]
        [TestCase("Blocked", TransferFailure.Blocked)]
        [TestCase("NotFound", TransferFailure.NotFound)]
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

            var againOutput = Stored(again).OutputPath;

            Subject.Remove(again.DownloadId, true);

            Directory.Exists(againOutput).Should().BeFalse();
            Directory.Exists(output).Should().BeTrue();
        }

        // Two releases named alike, from two friends or from one choosing the name: neither is
        // lost. The second goes next to the first, and its Arr app is told where.
        [Test]
        public void a_second_download_of_the_same_name_should_not_replace_the_first()
        {
            GivenCandidate("candidate-2", "e-1-502", Title);

            var first = GivenDownloading();
            GivenTransport(first, FileTransferState.Completed, 1000);
            Subject.Tick();

            var second = Subject.Add("candidate-2", "tv");
            Subject.Tick();
            GivenTransport(second, FileTransferState.Completed, 1000);
            Subject.Tick();

            var folder = Path.Combine(TempFolder, "complete", "tv", Title);

            Stored(first).OutputPath.Should().Be(folder);
            Stored(second).Should().Match<TransferJob>(j => j.State == TransferState.Completed && j.OutputPath == folder + ".1");
            File.Exists(Path.Combine(folder, Title + ".mkv")).Should().BeTrue();
            File.Exists(Path.Combine(folder + ".1", Title + ".mkv")).Should().BeTrue();
        }

        [Test]
        public void each_further_download_of_the_same_name_should_have_a_folder_of_its_own()
        {
            var folder = Path.Combine(TempFolder, "complete", "tv", Title);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(folder + ".1");

            var job = GivenDownloading();
            GivenTransport(job, FileTransferState.Completed, 1000);
            Subject.Tick();

            Stored(job).OutputPath.Should().Be(folder + ".2");
            Directory.Exists(folder).Should().BeTrue();
            Directory.Exists(folder + ".1").Should().BeTrue();
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

        // --- Identical copies on other nodes -----------------------------------------------------

        private Peer GivenPeer(string name, ConnectionPath path = ConnectionPath.Unknown, PeerState state = PeerState.Trusted)
        {
            return Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = name.ToLowerInvariant(), Name = name, State = state, ConnectionPath = path, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });
        }

        // Alice's file, and the same bytes on other nodes. The transport reports its hash as hash-1.
        private void GivenCopies(params (Peer Peer, long Size)[] others)
        {
            var copies = new List<PeerCatalogItem> { new() { PeerId = _alice.Id, CandidateId = "candidate-1", RemoteId = "e-1-501", Size = 1000, Blake3 = "hash-1" } };

            copies.AddRange(others.Select(o => new PeerCatalogItem { PeerId = o.Peer.Id, CandidateId = "candidate-" + o.Peer.NodeId, RemoteId = "e-7-" + o.Peer.Id, Size = o.Size, Blake3 = "hash-1" }));

            Mocker.GetMock<ICandidateService>().Setup(c => c.Copies("hash-1")).Returns(copies);
        }

        private TransferJob FailFromAlice(string kind)
        {
            var job = GivenDownloading();

            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();
            GivenTransport(job, FileTransferState.Failed, 400, kind);
            Subject.Tick();

            return Stored(job);
        }

        [TestCase("NoDirectRoute")]
        [TestCase("PeerUnreachable")]
        [TestCase("ConnectionLost")]
        [TestCase("NotAuthorized")]
        [TestCase("NotFound")]
        public void a_node_that_fails_should_be_left_at_once_for_another_with_the_same_file(string kind)
        {
            var bob = GivenPeer("Bob");
            GivenCopies((bob, 1000));

            var moved = FailFromAlice(kind);

            // Moved, and started from Bob in the same pass: its first attempt, not Alice's fourth.
            moved.Should().Match<TransferJob>(j => j.PeerId == bob.Id && j.PeerNodeId == "bob" && j.RemoteId == "e-7-" + bob.Id && j.State == TransferState.Downloading && j.Attempts == 1);
            moved.FailureMessage.Should().EndWith("Carrying on from Bob, which has the same file.");
            moved.TriedPeerIds.Should().Be($"{_alice.Id}");

            // What arrived from Alice is the start of the same file, so Bob carries on from it.
            File.Exists(_folders.IncompleteFile(moved)).Should().BeTrue();
            _started.Last().Should().Match<FileTransferRequest>(r => r.Peer.NodeId == "bob" && r.File == "e-7-" + bob.Id && r.ExpectedHash == "hash-1");
        }

        [Test]
        public void trust_ending_with_one_node_should_move_its_transfers_to_another_with_the_same_file()
        {
            var bob = GivenPeer("Bob");
            GivenCopies((bob, 1000));

            var job = GivenDownloading();
            GivenTransport(job, FileTransferState.Receiving, 400);
            Subject.Tick();

            _alice.State = PeerState.Revoked;
            Mocker.Resolve<IPeerRepository>().Update(_alice);
            Subject.Handle(new PeerStateChangedEvent(_alice, PeerState.Trusted));

            Stored(job).Should().Match<TransferJob>(j => j.PeerId == bob.Id && j.State == TransferState.Queued);
        }

        [Test]
        public void with_no_other_copy_a_failure_should_end_as_before()
        {
            GivenCopies();

            FailFromAlice("NotAuthorized").Should().Match<TransferJob>(j => j.State == TransferState.Failed && j.Failure == TransferFailure.NotAuthorized && j.PeerId == _alice.Id);
        }

        [Test]
        public void a_node_once_left_should_not_be_gone_back_to()
        {
            var bob = GivenPeer("Bob");
            GivenCopies((bob, 1000));

            var job = FailFromAlice("NoDirectRoute");

            GivenTransport(job, FileTransferState.Failed, 400, "NotFound");
            Subject.Tick();

            Stored(job).Should().Match<TransferJob>(j => j.State == TransferState.Failed && j.Failure == TransferFailure.NotFound && j.PeerId == bob.Id);
        }

        [Test]
        public void a_file_that_did_not_match_its_hash_should_be_tried_again_from_the_same_node()
        {
            var bob = GivenPeer("Bob");
            GivenCopies((bob, 1000));

            FailFromAlice("VerificationFailed").Should().Match<TransferJob>(j => j.PeerId == _alice.Id && j.State == TransferState.Queued && j.Failure == TransferFailure.VerificationFailed);
        }

        [Test]
        public void a_copy_of_another_size_should_not_be_taken_for_the_same_file()
        {
            var bob = GivenPeer("Bob");
            GivenCopies((bob, 999));

            FailFromAlice("NotAuthorized").State.Should().Be(TransferState.Failed);
        }

        [Test]
        public void a_node_last_reached_directly_should_be_tried_first()
        {
            var bob = GivenPeer("Bob", ConnectionPath.Relay);
            var carol = GivenPeer("Carol", ConnectionPath.Direct);
            GivenCopies((bob, 1000), (carol, 1000));

            FailFromAlice("NoDirectRoute").PeerId.Should().Be(carol.Id);
        }

        [Test]
        public void a_node_that_is_not_trusted_should_not_be_moved_to()
        {
            var mallory = GivenPeer("Mallory", state: PeerState.Revoked);
            GivenCopies((mallory, 1000));

            FailFromAlice("NotAuthorized").State.Should().Be(TransferState.Failed);
        }

        [Test]
        public void the_attempt_that_failed_should_be_put_down_to_the_node_it_was_with()
        {
            var bob = GivenPeer("Bob");
            GivenCopies((bob, 1000));
            var ended = new List<int>();

            Mocker.GetMock<IEventAggregator>()
                  .Setup(e => e.PublishEvent(It.IsAny<TransferAttemptEndedEvent>()))
                  .Callback<TransferAttemptEndedEvent>(e => ended.Add(e.PeerId));

            FailFromAlice("NoDirectRoute");

            ended.Should().Equal(_alice.Id);
        }

        [Test]
        public void a_transfer_should_know_the_hash_it_was_offered_with_before_anything_arrives()
        {
            var bob = GivenPeer("Bob");
            GivenCopies((bob, 1000));
            Mocker.GetMock<ICandidateService>()
                  .Setup(c => c.Find("candidate-1"))
                  .Returns(new PeerCatalogItem { PeerId = _alice.Id, CandidateId = "candidate-1", RemoteId = "e-1-501", MediaKind = MediaKind.Tv, ReleaseTitle = Title, Extension = ".mkv", Size = 1000, Blake3 = "hash-1" });

            var job = Subject.Add("candidate-1", "tv");
            Subject.Tick();

            // Alice could not be reached at all, so no hash was ever said for this transfer.
            _progress.Add(new FileTransferProgress { Id = job.DownloadId, State = FileTransferState.Failed, FailureKind = "NoRoute", FailureMessage = "detail" });
            Subject.Tick();

            Stored(job).Should().Match<TransferJob>(j => j.Blake3 == "hash-1" && j.Hash == null && j.PeerId == bob.Id);
        }
    }
}

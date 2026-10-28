using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
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
    // An album is fetched track by track, and appears for Lidarr only once every track has
    // arrived and been verified (ADR 0020).
    [TestFixture]
    public class AlbumTransferFixture : DbTest<TransferService, TransferJob>
    {
        private const string Title = "Low - Things We Lost in the Fire (2001) [FLAC]";

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
            Mocker.GetMock<INodeSettingsProvider>().Setup(s => s.Get()).Returns(new NodeSettings { MaxDownloads = NodeSettingsDefaults.MaxDownloads });
            _folders = new TransferFolders(Mocker.GetMock<INodeSettingsProvider>().Object);
            Mocker.SetConstant(_folders);

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted, IpAddresses = "192.0.2.1:4000", Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });

            Mocker.GetMock<ICandidateService>()
                  .Setup(c => c.Find("album"))
                  .Returns(new PeerCatalogItem
                  {
                      PeerId = _alice.Id, CandidateId = "album", RemoteId = "a-3-70", MediaKind = MediaKind.Music, Artist = "Low", Title = "Things We Lost in the Fire",
                      ReleaseTitle = Title, Size = 999, Blake3 = new string('a', 64),
                      Tracks = "[{\"Id\":\"t-3-701\",\"Extension\":\".flac\",\"Size\":100},{\"Id\":\"t-3-702\",\"Extension\":\".flac\",\"Size\":200}]"
                  });

            Mocker.GetMock<ICandidateService>()
                  .Setup(c => c.Find("broken"))
                  .Returns(new PeerCatalogItem { PeerId = _alice.Id, CandidateId = "broken", RemoteId = "a-3-71", MediaKind = MediaKind.Music, ReleaseTitle = "Broken", Size = 1 });

            Mocker.GetMock<ICandidateService>().Setup(c => c.Copies(It.IsAny<string>())).Returns(new List<PeerCatalogItem>());

            const string Titled = "[{\"Id\":\"t-3-801\",\"Extension\":\".flac\",\"Size\":10,\"Title\":\"Breadcrumb Trail\",\"Number\":1,\"Disc\":1}," +
                "{\"Id\":\"t-3-802\",\"Extension\":\".flac\",\"Size\":10,\"Title\":\"Nosferatu Man\",\"Number\":2,\"Disc\":1}," +
                "{\"Id\":\"t-3-803\",\"Extension\":\".flac\",\"Size\":10,\"Number\":3,\"Disc\":1}]";

            const string Deluxe = "[{\"Id\":\"t-3-901\",\"Extension\":\".flac\",\"Size\":10,\"Title\":\"Breadcrumb Trail\",\"Number\":1,\"Disc\":1}," +
                "{\"Id\":\"t-3-902\",\"Extension\":\".flac\",\"Size\":10,\"Title\":\"Nosferatu Man (demo)\",\"Number\":1,\"Disc\":2}," +
                "{\"Id\":\"t-3-903\",\"Extension\":\".flac\",\"Size\":10,\"Title\":\"Interlude\",\"Number\":2,\"Disc\":2}," +
                "{\"Id\":\"t-3-904\",\"Extension\":\".flac\",\"Size\":10,\"Title\":\"Interlude\",\"Number\":2,\"Disc\":2}]";

            GivenAlbum("titled", "Slint", "Spiderland", Titled);
            GivenAlbum("deluxe", "Slint", "Spiderland", Deluxe);
            GivenAlbum("unsafe", "AC/DC", "Who Made Who?", "[{\"Id\":\"t-3-951\",\"Extension\":\".flac\",\"Size\":10,\"Title\":\"Shake <Your> Foundations: Live\",\"Number\":1}]");

            _progress = new List<FileTransferProgress>();
            _started = new List<FileTransferRequest>();

            Mocker.GetMock<IFileTransport>().Setup(t => t.List()).Returns(() => _progress.ToList());
            Mocker.GetMock<IFileTransport>().Setup(t => t.Start(It.IsAny<FileTransferRequest>())).Callback<FileTransferRequest>(_started.Add);
        }

        private void GivenAlbum(string candidateId, string artist, string album, string tracks)
        {
            Mocker.GetMock<ICandidateService>()
                  .Setup(c => c.Find(candidateId))
                  .Returns(new PeerCatalogItem
                  {
                      PeerId = _alice.Id, CandidateId = candidateId, RemoteId = "a-" + candidateId, MediaKind = MediaKind.Music, Artist = artist, Title = album,
                      ReleaseTitle = $"{artist} - {album} (1991) [FLAC]", Size = 1, Tracks = tracks
                  });
        }

        private List<string> FileNames(string candidateId)
        {
            var job = Subject.Add(candidateId, "music");

            return job.GetParts().Select((_, i) =>
            {
                job.PartIndex = i;

                return Path.GetFileName(_folders.IncompleteFile(job));
            }).ToList();
        }

        [Test]
        public void tracks_should_be_named_as_lidarr_names_its_own()
        {
            FileNames("titled").Should().Equal(
                "Slint - Spiderland - 01 - Breadcrumb Trail.flac",
                "Slint - Spiderland - 02 - Nosferatu Man.flac",
                "Slint - Spiderland (1991) [FLAC] - 03.flac");
        }

        [Test]
        public void tracks_of_an_album_on_several_discs_should_be_numbered_by_disc_and_never_named_alike()
        {
            FileNames("deluxe").Should().Equal(
                "Slint - Spiderland - 1-01 - Breadcrumb Trail.flac",
                "Slint - Spiderland - 2-01 - Nosferatu Man (demo).flac",
                "Slint - Spiderland - 2-02 - Interlude.flac",
                "Slint - Spiderland - 2-02 - Interlude (2).flac");
        }

        [Test]
        public void a_track_name_should_hold_nothing_a_file_name_cannot()
        {
            FileNames("unsafe").Should().Equal("ACDC - Who Made Who - 01 - Shake Your Foundations Live.flac");
        }

        [Test]
        public void a_very_long_track_name_should_be_cut_short()
        {
            GivenAlbum("long", "Artist", "Album", "[{\"Id\":\"t-3-961\",\"Extension\":\".flac\",\"Size\":10,\"Title\":\"" + new string('x', 290) + "\",\"Number\":1}]");

            FileNames("long").Single().Length.Should().Be(150 + ".flac".Length);
        }

        [TearDown]
        public void IgnoreExpectedWarnings()
        {
            ExceptionVerification.IgnoreWarns();
        }

        private TransferJob Stored(TransferJob job) => Subject.Find(job.DownloadId);

        // The transport reports on the part under way, and writes it as the sidecar would.
        private void GivenPart(TransferJob job, FileTransferState state, long bytesDone, long size, string failure = null)
        {
            job = Stored(job);
            _progress.RemoveAll(p => p.Id == job.TransportId);
            _progress.Add(new FileTransferProgress { Id = job.TransportId, State = state, BytesDone = bytesDone, Size = size, Hash = "hash-" + job.PartIndex, FailureKind = failure, FailureMessage = failure == null ? null : "detail" });

            if (state is FileTransferState.Receiving or FileTransferState.Completed)
            {
                Directory.CreateDirectory(_folders.IncompleteFolder(job));
                File.WriteAllText(_folders.IncompleteFile(job), "bytes " + job.PartIndex);
            }
        }

        [Test]
        public void an_album_should_become_one_transfer_of_its_tracks()
        {
            var job = Subject.Add("album", "music");

            job.HasParts.Should().BeTrue();
            job.GetParts().Select(p => p.RemoteId).Should().Equal("t-3-701", "t-3-702");
            job.RemoteId.Should().Be("t-3-701");
            job.Size.Should().Be(300);
            job.Blake3.Should().BeNull("an album as a whole has no hash");
        }

        [Test]
        public void an_album_offered_without_tracks_should_not_be_taken()
        {
            Subject.Add("broken", "music").Should().BeNull();
        }

        [Test]
        public void the_tracks_should_be_fetched_one_after_another_into_one_folder()
        {
            var job = Subject.Add("album", "music");

            Subject.Tick();

            _started.Should().ContainSingle();
            _started[0].File.Should().Be("t-3-701");
            _started[0].Destination.Should().Be(Path.Combine(_folders.IncompleteFolder(job), Title + " - 01.flac"));

            GivenPart(job, FileTransferState.Completed, 100, 100);
            Subject.Tick();

            _started.Should().HaveCount(2);
            _started[1].File.Should().Be("t-3-702");
            _started[1].Id.Should().NotBe(_started[0].Id);
            _started[1].Destination.Should().Be(Path.Combine(_folders.IncompleteFolder(job), Title + " - 02.flac"));
            _started[1].ExpectedHash.Should().BeNull("the hash was the first track's");

            Stored(job).State.Should().Be(TransferState.Downloading);
            Stored(job).BytesDone.Should().Be(100);
            Directory.Exists(_folders.CompleteFolder(job)).Should().BeFalse("nothing appears until every track has arrived");
        }

        [Test]
        public void progress_should_count_the_tracks_already_arrived()
        {
            var job = Subject.Add("album", "music");

            Subject.Tick();
            GivenPart(job, FileTransferState.Completed, 100, 100);
            Subject.Tick();
            GivenPart(job, FileTransferState.Receiving, 50, 200);
            Subject.Tick();

            Stored(job).BytesDone.Should().Be(150);
            Stored(job).Size.Should().Be(300);
        }

        [Test]
        public void the_album_should_appear_whole_once_the_last_track_is_verified()
        {
            var job = Subject.Add("album", "music");

            Subject.Tick();
            GivenPart(job, FileTransferState.Completed, 100, 100);
            Subject.Tick();
            GivenPart(job, FileTransferState.Completed, 200, 200);
            Subject.Tick();

            var done = Stored(job);

            done.State.Should().Be(TransferState.Completed);
            done.OutputPath.Should().Be(_folders.CompleteFolder(job));
            Directory.GetFiles(done.OutputPath).Select(Path.GetFileName).Should().BeEquivalentTo(Title + " - 01.flac", Title + " - 02.flac");
            Directory.Exists(_folders.IncompleteFolder(job)).Should().BeFalse();
        }

        [Test]
        public void a_track_that_fails_for_good_should_fail_the_album_and_leave_nothing()
        {
            var job = Subject.Add("album", "music");

            Subject.Tick();
            GivenPart(job, FileTransferState.Completed, 100, 100);
            Subject.Tick();
            GivenPart(job, FileTransferState.Failed, 0, 200, "NotAuthorized");
            Subject.Tick();

            Stored(job).State.Should().Be(TransferState.Failed);
            Directory.Exists(_folders.IncompleteFolder(job)).Should().BeFalse();
            Directory.Exists(_folders.CompleteFolder(job)).Should().BeFalse();
        }

        [Test]
        public void a_track_that_did_not_match_its_hash_should_start_over_and_keep_the_tracks_before_it()
        {
            var job = Subject.Add("album", "music");

            Subject.Tick();
            GivenPart(job, FileTransferState.Completed, 100, 100);
            Subject.Tick();
            GivenPart(job, FileTransferState.Receiving, 50, 200);
            Subject.Tick();
            GivenPart(job, FileTransferState.Failed, 50, 200, "VerificationFailed");
            Subject.Tick();

            var again = Stored(job);

            again.State.Should().Be(TransferState.Queued);
            again.PartIndex.Should().Be(1);
            again.BytesDone.Should().Be(100);
            File.Exists(Path.Combine(_folders.IncompleteFolder(job), Title + " - 01.flac")).Should().BeTrue();
            File.Exists(Path.Combine(_folders.IncompleteFolder(job), Title + " - 02.flac")).Should().BeFalse();
        }

        [Test]
        public void an_album_should_not_move_to_another_node_with_one_of_its_hashes()
        {
            Mocker.GetMock<ICandidateService>().Setup(c => c.Copies(It.IsAny<string>())).Returns(new List<PeerCatalogItem> { new() { PeerId = 99, CandidateId = "other", Size = 200 } });

            var job = Subject.Add("album", "music");

            Subject.Tick();
            GivenPart(job, FileTransferState.Failed, 0, 100, "NotFound");
            Subject.Tick();

            Stored(job).PeerId.Should().Be(_alice.Id);
        }
    }
}

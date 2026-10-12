using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Transfers
{
    [TestFixture]
    public class UploadTrackerFixture : CoreTest<UploadTracker>
    {
        private List<FileUploadProgress> _reported;

        [SetUp]
        public void Setup()
        {
            _reported = new List<FileUploadProgress>();

            Mocker.GetMock<IFileTransport>().Setup(t => t.ListUploads()).Returns(() => _reported.ToList());
            Mocker.GetMock<IPeerRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<Peer> { new() { NodeId = "bob", Name = "Bob" } });
        }

        private FileUploadProgress GivenUpload(string id, UploadState state, long sent, long size = 1000, string path = "/tv/Pingu/Season 1/Pingu.S01E01.mkv", string node = "bob", string failure = null)
        {
            _reported.RemoveAll(u => u.Id == id);

            var upload = new FileUploadProgress { Id = id, RemoteNodeId = node, File = "e-1-501", Path = path, State = state, BytesSent = sent, Size = size, FailureKind = failure };

            _reported.Add(upload);

            return upload;
        }

        private int Announcements => Mocker.GetMock<IEventAggregator>().Invocations.Count(i => i.Arguments.Count > 0 && i.Arguments[0] is TransfersChangedEvent);

        [Test]
        public void an_upload_should_be_shown_with_who_it_is_going_to_and_what_it_is()
        {
            GivenUpload("1", UploadState.Sending, 400);

            Subject.Tick();

            Subject.All().Should().ContainSingle().Which.Should().Match<Upload>(u =>
                u.PeerName == "Bob" && u.Title == "Pingu.S01E01.mkv" && u.State == UploadState.Sending && u.BytesSent == 400 && u.Size == 1000 && u.Ended == null);
        }

        [Test]
        public void a_file_on_a_windows_path_should_be_named_the_same_way()
        {
            GivenUpload("1", UploadState.Sending, 0, path: @"D:\Media\TV\Pingu\Pingu.S01E01.mkv");

            Subject.Tick();

            Subject.All().Single().Title.Should().Be("Pingu.S01E01.mkv");
        }

        [Test]
        public void an_upload_to_a_node_that_has_since_been_deleted_should_still_be_shown()
        {
            GivenUpload("1", UploadState.Sending, 0, node: "someone-else");

            Subject.Tick();

            Subject.All().Single().PeerName.Should().BeNull();
        }

        [Test]
        public void progress_should_be_followed_and_announced_only_when_it_moves()
        {
            GivenUpload("1", UploadState.Sending, 400);
            Subject.Tick();
            Subject.Tick();

            Announcements.Should().Be(1);

            GivenUpload("1", UploadState.Sending, 800);
            Subject.Tick();

            Subject.All().Single().BytesSent.Should().Be(800);
            Announcements.Should().Be(2);
        }

        [Test]
        public void a_finished_upload_should_stay_listed_after_the_transport_forgets_it()
        {
            GivenUpload("1", UploadState.Sending, 400);
            Subject.Tick();
            GivenUpload("1", UploadState.Completed, 1000);
            Subject.Tick();

            _reported.Clear();
            Subject.Tick();

            Subject.All().Single().Should().Match<Upload>(u => u.State == UploadState.Completed && u.BytesSent == 1000 && u.Ended != null);
        }

        [Test]
        public void a_failed_upload_should_say_why()
        {
            GivenUpload("1", UploadState.Failed, 300, failure: "Revoked");

            Subject.Tick();

            Subject.All().Single().Should().Match<Upload>(u => u.State == UploadState.Failed && u.Failure == "Revoked" && u.Ended != null);
        }

        [Test]
        public void an_upload_that_vanishes_part_way_should_be_marked_as_stopped()
        {
            GivenUpload("1", UploadState.Sending, 400);
            Subject.Tick();

            // The transport was restarted.
            _reported.Clear();
            Subject.Tick();

            Subject.All().Single().Should().Match<Upload>(u => u.State == UploadState.Failed && u.Failure == "Stopped" && u.BytesSent == 400);
        }

        [Test]
        public void uploads_under_way_should_stop_when_the_transport_is_not_running()
        {
            GivenUpload("1", UploadState.Sending, 400);
            Subject.Tick();

            Mocker.GetMock<IFileTransport>().Setup(t => t.ListUploads()).Throws(new TransportException(TransportFailure.Unavailable, "not running"));
            Subject.Tick();

            Subject.All().Single().State.Should().Be(UploadState.Failed);
        }

        [Test]
        public void what_is_under_way_should_be_listed_first_then_the_newest()
        {
            GivenUpload("1", UploadState.Completed, 1000);
            Subject.Tick();
            GivenUpload("2", UploadState.Sending, 10);
            Subject.Tick();
            GivenUpload("3", UploadState.Completed, 1000);
            Subject.Tick();

            Subject.All().First().Id.Should().Be("2");
        }

        [Test]
        public void only_so_many_finished_uploads_should_be_kept()
        {
            for (var i = 0; i < UploadTracker.MaxFinished + 20; i++)
            {
                GivenUpload(i.ToString(), UploadState.Completed, 1000);
            }

            Subject.Tick();
            GivenUpload("live", UploadState.Sending, 1);
            Subject.Tick();

            Subject.All().Should().HaveCount(UploadTracker.MaxFinished + 1);
            Subject.All().Should().Contain(u => u.Id == "live");
        }

        [Test]
        public void a_reader_should_not_be_handed_what_is_still_being_updated()
        {
            GivenUpload("1", UploadState.Sending, 400);
            Subject.Tick();

            var read = Subject.All().Single();

            GivenUpload("1", UploadState.Sending, 900);
            Subject.Tick();

            read.BytesSent.Should().Be(400);
        }
    }
}

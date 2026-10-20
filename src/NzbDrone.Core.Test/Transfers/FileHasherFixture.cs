using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Transfers
{
    [TestFixture]
    public class FileHasherFixture : DbTest<FileHasher, FileHash>
    {
        private readonly Peer _bob = new() { Id = 2, NodeId = "bob", Name = "Bob", State = PeerState.Trusted };
        private readonly Dictionary<Peer, List<OfferItem>> _offers = new();
        private readonly List<Upload> _uploads = new();
        private readonly List<string> _read = new();
        private readonly HashSet<string> _unreadable = new();

        [SetUp]
        public void Setup()
        {
            _offers.Clear();
            _uploads.Clear();
            _read.Clear();
            _unreadable.Clear();

            Mocker.SetConstant<IFileHashRepository>(Mocker.Resolve<FileHashRepository>());

            Mocker.GetMock<IPeerRepository>().Setup(p => p.All()).Returns(() => _offers.Keys.ToList());
            Mocker.GetMock<ISharingService>().Setup(s => s.BuildOffer(It.IsAny<Peer>(), It.IsAny<bool>())).Returns((Peer peer, bool _) => new Offer { Items = _offers[peer] });
            Mocker.GetMock<ICatalogFileLocator>()
                  .Setup(l => l.Locate(It.IsAny<IEnumerable<string>>()))
                  .Returns((IEnumerable<string> ids) => ids.ToDictionary(id => id, id => _unreadable.Contains(id)
                      ? new LocatedFile { LocalPath = "/tv/" + id + ".mkv", Exists = false }
                      : new LocatedFile { LocalPath = "/library/" + id + ".mkv", Exists = true }));
            Mocker.GetMock<IUploadTracker>().Setup(u => u.All()).Returns(() => _uploads.ToList());

            // The transport reads a file only when it was not told a hash that still fits.
            Mocker.GetMock<IFileTransport>()
                  .Setup(t => t.Hash(It.IsAny<string>(), It.IsAny<HashedFile>()))
                  .Returns((string path, HashedFile known) =>
                  {
                      var current = new HashedFile { Size = 100, ModifiedNs = 5, Blake3 = "hash-of-" + path };

                      if (known == null || known.Size != current.Size || known.ModifiedNs != current.ModifiedNs)
                      {
                          _read.Add(path);
                      }

                      return current;
                  });
        }

        private void GivenShared(Peer peer, params OfferItem[] items)
        {
            _offers[peer] = items.ToList();
        }

        private static OfferItem Item(string id, int daysAgo = 1)
        {
            return new OfferItem { Id = id, DateAdded = DateTime.UtcNow.AddDays(-daysAgo) };
        }

        [Test]
        public void every_shared_file_should_be_hashed_and_the_hash_kept()
        {
            GivenShared(_bob, Item("e-1-501"), Item("m-2-701"));

            Subject.HashShared(CancellationToken.None).Should().BeTrue();

            _read.Should().BeEquivalentTo("/library/e-1-501.mkv", "/library/m-2-701.mkv");
            Subject.Known("/library/e-1-501.mkv").Should().Match<HashedFile>(h => h.Blake3 == "hash-of-/library/e-1-501.mkv" && h.Size == 100 && h.ModifiedNs == 5);
            Subject.Status().Should().Match<HashingStatus>(s => s.Shared == 2 && s.Hashed == 2 && s.Current == null && s.LastPass != null);
        }

        [Test]
        public void each_kept_hash_should_say_which_offered_file_it_is_for()
        {
            GivenShared(_bob, Item("e-1-501"), Item("m-2-701"));
            Db.Insert(new FileHash { Path = "/library/e-1-501.mkv", Size = 100, ModifiedNs = 5, Blake3 = "hash-of-/library/e-1-501.mkv", Hashed = DateTime.UtcNow });

            Subject.HashShared(CancellationToken.None);

            Db.All<FileHash>().Select(h => (h.Path, h.OfferFileId)).Should().BeEquivalentTo(new[] { ("/library/e-1-501.mkv", "e-1-501"), ("/library/m-2-701.mkv", "m-2-701") });
        }

        [Test]
        public void the_newest_files_should_be_hashed_first()
        {
            GivenShared(_bob, Item("e-1-old", 30), Item("e-1-new", 1), Item("e-1-middle", 10));

            Subject.HashShared(CancellationToken.None);

            _read.Should().Equal("/library/e-1-new.mkv", "/library/e-1-middle.mkv", "/library/e-1-old.mkv");
        }

        [Test]
        public void a_file_hashed_before_should_not_be_read_again_while_it_is_unchanged()
        {
            GivenShared(_bob, Item("e-1-501"));
            Subject.HashShared(CancellationToken.None);
            _read.Clear();

            Subject.HashShared(CancellationToken.None);

            _read.Should().BeEmpty();
            Mocker.GetMock<IFileTransport>().Verify(t => t.Hash("/library/e-1-501.mkv", It.Is<HashedFile>(k => k != null && k.Blake3 == "hash-of-/library/e-1-501.mkv")), Times.Once());
        }

        [Test]
        public void a_file_that_changed_should_have_its_hash_replaced()
        {
            GivenShared(_bob, Item("e-1-501"));
            Db.Insert(new FileHash { Path = "/library/e-1-501.mkv", Size = 90, ModifiedNs = 1, Blake3 = "old", Hashed = DateTime.UtcNow.AddDays(-1) });

            Subject.HashShared(CancellationToken.None);

            _read.Should().Equal("/library/e-1-501.mkv");
            Subject.Known("/library/e-1-501.mkv").Blake3.Should().Be("hash-of-/library/e-1-501.mkv");
            Db.All<FileHash>().Should().HaveCount(1);
        }

        [Test]
        public void nothing_should_be_hashed_that_no_trusted_node_is_offered()
        {
            GivenShared(new Peer { Id = 3, NodeId = "carol", State = PeerState.Revoked }, Item("e-1-501"));

            Subject.HashShared(CancellationToken.None);

            _read.Should().BeEmpty();
            Mocker.GetMock<ISharingService>().Verify(s => s.BuildOffer(It.IsAny<Peer>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void a_file_offered_to_two_nodes_should_be_hashed_once()
        {
            GivenShared(_bob, Item("e-1-501"));
            GivenShared(new Peer { Id = 3, NodeId = "carol", State = PeerState.Trusted }, Item("e-1-501"));

            Subject.HashShared(CancellationToken.None);

            _read.Should().Equal("/library/e-1-501.mkv");
        }

        [Test]
        public void a_file_that_cannot_be_read_here_should_be_skipped()
        {
            GivenShared(_bob, Item("e-1-501"), Item("e-1-502"));
            _unreadable.Add("e-1-501");

            Subject.HashShared(CancellationToken.None);

            _read.Should().Equal("/library/e-1-502.mkv");
            Subject.Status().Shared.Should().Be(1);
        }

        [Test]
        public void a_file_the_transport_cannot_read_should_be_left_without_a_hash()
        {
            GivenShared(_bob, Item("e-1-501"));
            Mocker.GetMock<IFileTransport>().Setup(t => t.Hash(It.IsAny<string>(), It.IsAny<HashedFile>())).Returns((HashedFile)null);

            Subject.HashShared(CancellationToken.None).Should().BeTrue();

            Db.All<FileHash>().Should().BeEmpty();
            Subject.Status().Hashed.Should().Be(0);
        }

        [Test]
        public void a_transport_that_is_not_running_should_end_the_pass_to_be_tried_again()
        {
            GivenShared(_bob, Item("e-1-501"), Item("e-1-502"));
            Mocker.GetMock<IFileTransport>().Setup(t => t.Hash(It.IsAny<string>(), It.IsAny<HashedFile>())).Throws(new TransportException(TransportFailure.Unavailable, "not running"));

            Subject.HashShared(CancellationToken.None).Should().BeFalse();

            Subject.Status().Should().Match<HashingStatus>(s => s.Current == null && s.LastPass == null);
        }

        [Test]
        public void a_new_file_should_wait_while_this_node_is_sending()
        {
            GivenShared(_bob, Item("e-1-501"));
            _uploads.Add(new Upload { Id = "7", State = UploadState.Sending });

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            Subject.HashShared(cancellation.Token).Should().BeFalse();

            _read.Should().BeEmpty();
            Subject.Status().WaitingForUploads.Should().BeTrue();
        }

        [Test]
        public void a_file_already_hashed_should_be_checked_even_while_this_node_is_sending()
        {
            GivenShared(_bob, Item("e-1-501"));
            Subject.HashShared(CancellationToken.None);
            _uploads.Add(new Upload { Id = "7", State = UploadState.Sending });

            Subject.HashShared(CancellationToken.None).Should().BeTrue();
        }

        [Test]
        public void a_hash_for_a_file_no_longer_shared_should_be_kept_a_while_then_dropped()
        {
            GivenShared(_bob, Item("e-1-501"));
            Db.Insert(new FileHash { Path = "/library/recent.mkv", Size = 1, ModifiedNs = 1, Blake3 = "a", Hashed = DateTime.UtcNow.AddDays(-2) });
            Db.Insert(new FileHash { Path = "/library/long-gone.mkv", Size = 1, ModifiedNs = 1, Blake3 = "b", Hashed = DateTime.UtcNow.AddDays(-60) });

            Subject.HashShared(CancellationToken.None);

            Db.All<FileHash>().Select(h => h.Path).Should().BeEquivalentTo("/library/e-1-501.mkv", "/library/recent.mkv");
        }

        [Test]
        public void a_hash_the_other_node_found_wrong_should_be_forgotten_and_worked_out_again()
        {
            GivenShared(_bob, Item("e-1-501"), Item("e-1-502"));
            Subject.HashShared(CancellationToken.None);
            _read.Clear();

            Subject.Handle(new UploadVerificationFailedEvent("/library/e-1-501.mkv"));

            Subject.Known("/library/e-1-501.mkv").Should().BeNull();
            Subject.Known("/library/e-1-502.mkv").Should().NotBeNull();

            Subject.HashShared(CancellationToken.None);

            _read.Should().Equal("/library/e-1-501.mkv");
        }

        [Test]
        public void a_mismatch_for_a_file_with_no_kept_hash_should_change_nothing()
        {
            Subject.Handle(new UploadVerificationFailedEvent("/library/unknown.mkv"));

            Db.All<FileHash>().Should().BeEmpty();
        }

        [Test]
        public void a_hash_for_a_shared_file_should_be_kept_however_old_it_is()
        {
            GivenShared(_bob, Item("e-1-501"));
            Db.Insert(new FileHash { Path = "/library/e-1-501.mkv", Size = 100, ModifiedNs = 5, Blake3 = "hash-of-/library/e-1-501.mkv", Hashed = DateTime.UtcNow.AddDays(-400) });

            Subject.HashShared(CancellationToken.None);

            Db.All<FileHash>().Should().HaveCount(1);
            _read.Should().BeEmpty();
        }
    }
}

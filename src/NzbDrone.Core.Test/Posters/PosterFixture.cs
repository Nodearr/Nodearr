using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Posters;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Posters
{
    public static class PosterBytes
    {
        // The first bytes of a JPEG, which is all the check looks at.
        public static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46 };
    }

    [TestFixture]
    public class PosterCacheFixture : CoreTest
    {
        private PosterCache _cache;

        [SetUp]
        public void Setup()
        {
            _cache = new PosterCache(Path.Combine(TempFolder, "posters"));
        }

        [Test]
        public void a_poster_kept_should_be_found_until_it_is_too_old()
        {
            _cache.Put("tv-75669", PosterBytes.Jpeg);

            _cache.TryGet("tv-75669", TimeSpan.FromDays(1), out var poster).Should().BeTrue();
            poster.Should().Equal(PosterBytes.Jpeg);

            File.SetLastWriteTimeUtc(Path.Combine(TempFolder, "posters", "tv-75669.img"), DateTime.UtcNow.AddDays(-2));

            _cache.TryGet("tv-75669", TimeSpan.FromDays(1), out _).Should().BeFalse();
        }

        [Test]
        public void none_should_be_known_until_its_time_is_up()
        {
            _cache.PutNone("tv-1", TimeSpan.FromHours(1));

            _cache.TryGet("tv-1", TimeSpan.FromDays(1), out var poster).Should().BeTrue();
            poster.Should().BeNull();

            _cache.PutNone("tv-2", TimeSpan.FromHours(-1));

            _cache.TryGet("tv-2", TimeSpan.FromDays(1), out _).Should().BeFalse();
        }

        [Test]
        public void a_poster_found_later_should_replace_none()
        {
            _cache.PutNone("tv-1", TimeSpan.FromHours(1));
            _cache.Put("tv-1", PosterBytes.Jpeg);

            _cache.TryGet("tv-1", TimeSpan.FromDays(1), out var poster).Should().BeTrue();
            poster.Should().Equal(PosterBytes.Jpeg);
        }

        [Test]
        public void a_key_that_is_not_a_plain_name_should_be_kept_under_its_hash()
        {
            _cache.Put("adult-../../x", PosterBytes.Jpeg);

            Directory.GetFiles(Path.Combine(TempFolder, "posters")).Should().ContainSingle()
                     .Which.Should().MatchRegex(@"[0-9a-f]{32}\.img$");
            _cache.TryGet("adult-../../x", TimeSpan.FromDays(1), out _).Should().BeTrue();
        }
    }

    [TestFixture]
    public class PosterImagesFixture
    {
        [Test]
        public void only_a_jpeg_or_a_png_should_do()
        {
            PosterImages.Checked(PosterBytes.Jpeg).Should().NotBeNull();
            PosterImages.Checked(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }).Should().NotBeNull();
            PosterImages.Checked(System.Text.Encoding.UTF8.GetBytes("<html>not found</html>")).Should().BeNull();
            PosterImages.Checked(null).Should().BeNull();
        }

        [Test]
        public void whisparr_images_should_be_fetched_by_their_path_resized_first()
        {
            var proxy = new Mock<IArrV3Proxy>();
            var images = JArray.Parse("[{\"coverType\":\"screenshot\",\"url\":\"/MediaCover/movie/1/screenshot.jpg?h=abc\"}]");

            proxy.Setup(p => p.GetBytes(It.IsAny<IArrConnection>(), "/MediaCover/movie/1/screenshot-500.jpg", false)).Returns((byte[])null);
            proxy.Setup(p => p.GetBytes(It.IsAny<IArrConnection>(), "/MediaCover/movie/1/screenshot.jpg", false)).Returns(PosterBytes.Jpeg);

            PosterImages.FromImages(proxy.Object, null, images, "poster", "screenshot").Should().Equal(PosterBytes.Jpeg);
        }
    }

    [TestFixture]
    public class PosterExchangeFixture : CoreTest<PosterExchange>
    {
        private readonly Peer _bob = new() { Id = 2, Name = "Bob", State = PeerState.Trusted };

        [Test]
        public void a_title_the_friend_is_offered_should_be_answered_with_its_poster()
        {
            var title = new PosterTitle(1, PosterSubject.Series, 10);

            Mocker.GetMock<ISharingService>().Setup(s => s.PosterFor(_bob, "e-1-501")).Returns(title);
            Mocker.GetMock<ILocalPosters>().Setup(l => l.Get(title)).Returns(PosterBytes.Jpeg);

            Subject.Answer(_bob, new PeerMessage { PosterFor = "e-1-501" }).Should().Match<PeerReply>(r => r.Ok && r.Poster.Length == PosterBytes.Jpeg.Length);
        }

        [Test]
        public void a_title_the_friend_is_not_offered_should_be_answered_as_one_without_a_poster()
        {
            Subject.Answer(_bob, new PeerMessage { PosterFor = "e-1-502" }).Should().Match<PeerReply>(r => r.Ok && r.Poster == null);

            Mocker.GetMock<ILocalPosters>().Verify(l => l.Get(It.IsAny<PosterTitle>()), Times.Never());
        }
    }

    [TestFixture]
    public class PeerPostersFixture : CoreTest<PeerPosters>
    {
        private Peer _alice;
        private Dictionary<int, PeerCatalogItem> _items;

        [SetUp]
        public void Setup()
        {
            WithTempAsAppPath();

            _alice = new Peer { Id = 1, Name = "Alice", State = PeerState.Trusted };
            _items = new Dictionary<int, PeerCatalogItem>
            {
                [10] = new() { Id = 10, PeerId = 1, RemoteId = "e-1-501", MediaKind = MediaKind.Tv, TvdbId = 75669 },
                [11] = new() { Id = 11, PeerId = 1, RemoteId = "e-1-502", MediaKind = MediaKind.Tv, TvdbId = 75669 },
                [12] = new() { Id = 12, PeerId = 3, RemoteId = "e-4-900", MediaKind = MediaKind.Tv, TvdbId = 75669 }
            };

            Mocker.GetMock<IPeerCatalogRepository>().Setup(r => r.Find(It.IsAny<int>())).Returns<int>(id => _items.GetValueOrDefault(id));
            Mocker.GetMock<IPeerRepository>().Setup(r => r.Find(It.IsAny<int>())).Returns<int>(id => id == 1 ? _alice : id == 3 ? new Peer { Id = 3, State = PeerState.Trusted } : null);
            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.Is<PeerMessage>(m => m.Type == PeerMessageTypes.Poster)))
                  .Returns(new PeerReply { Ok = true, Poster = PosterBytes.Jpeg });
        }

        [Test]
        public void a_series_poster_should_be_asked_for_once_whichever_file_or_friend_it_is_shown_for()
        {
            Subject.Get(10).Should().Equal(PosterBytes.Jpeg);
            Subject.Get(11).Should().Equal(PosterBytes.Jpeg);
            Subject.Get(12).Should().Equal(PosterBytes.Jpeg);

            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(_alice, It.Is<PeerMessage>(m => m.PosterFor == "e-1-501")), Times.Once());
            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Once());
        }

        [Test]
        public void a_title_without_a_poster_should_not_be_asked_about_again_for_a_while()
        {
            Mocker.GetMock<IPeerService>().Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>())).Returns(new PeerReply { Ok = true });

            Subject.Get(10).Should().BeNull();
            Subject.Get(11).Should().BeNull();

            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Once());
        }

        [Test]
        public void what_is_not_an_image_should_be_no_poster()
        {
            Mocker.GetMock<IPeerService>().Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>())).Returns(new PeerReply { Ok = true, Poster = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 } });

            Subject.Get(10).Should().BeNull();
        }

        [Test]
        public void an_older_or_unreachable_friend_should_mean_no_poster_for_now()
        {
            Mocker.GetMock<IPeerService>().Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>())).Throws(new TransportException(TransportFailure.NotAuthorized, "refused"));

            Subject.Get(10).Should().BeNull();
            Subject.Get(10).Should().BeNull();

            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Once());
        }

        // A poster moves only over a direct path. Without one there is no poster for now, and the
        // friend is asked again once a path may have appeared, not a day later.
        [Test]
        public void a_friend_with_no_direct_path_should_mean_no_poster_for_now()
        {
            Mocker.GetMock<IPeerService>().Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>())).Throws(new TransportException(TransportFailure.NoDirectRoute, "no direct path within 20s"));

            Subject.Get(10).Should().BeNull();
            Subject.Get(10).Should().BeNull();

            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(It.IsAny<Peer>(), It.Is<PeerMessage>(m => m.Type == PeerMessageTypes.Poster)), Times.Once());

            var askAgainAt = DateTime.Parse(File.ReadAllText(Directory.GetFiles(TempFolder, "*.none", SearchOption.AllDirectories).Should().ContainSingle().Subject), null, System.Globalization.DateTimeStyles.RoundtripKind);
            askAgainAt.Should().BeCloseTo(DateTime.UtcNow + PeerPosters.UnreachableFor, TimeSpan.FromMinutes(1));
        }

        [Test]
        public void a_friend_no_longer_trusted_should_not_be_asked()
        {
            _alice.State = PeerState.Revoked;

            Subject.Get(10).Should().BeNull();

            Mocker.GetMock<IPeerService>().Verify(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()), Times.Never());
        }

        [TestCase(MediaKind.Movie, false, 10331, null, null, "movie-10331")]
        [TestCase(MediaKind.Music, false, 0, "rg-1", null, "album-rg-1")]
        [TestCase(MediaKind.Movie, true, 10331, null, "stash-1", "adult-stash-1")]
        [TestCase(MediaKind.Tv, false, 0, null, null, "peer1-e-1-501")]
        public void a_title_should_be_known_by_what_every_node_knows_it_by(MediaKind kind, bool adult, int id, string album, string stash, string key)
        {
            var item = new PeerCatalogItem { PeerId = 1, RemoteId = "e-1-501", MediaKind = kind, IsAdult = adult, TmdbId = id, ForeignAlbumId = album, StashId = stash };

            PeerPosters.KeyOf(item).Should().Be(key);
        }
    }
}

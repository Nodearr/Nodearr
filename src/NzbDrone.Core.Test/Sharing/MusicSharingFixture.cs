using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Sharing
{
    // Lidarr's albums are shared by artist, and offered whole, with their tracks (ADR 0020).
    [TestFixture]
    public class MusicSharingFixture : DbTest<SharingService, ShareList>
    {
        private static readonly ShareKey Low = ShareKey.Artist(3, 7);
        private static readonly ShareKey Slint = ShareKey.Artist(3, 8);

        private Peer _bob;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IShareListRepository>(Mocker.Resolve<ShareListRepository>());
            Mocker.SetConstant<IShareListItemRepository>(Mocker.Resolve<ShareListItemRepository>());
            Mocker.SetConstant<IShareListPeerRepository>(Mocker.Resolve<ShareListPeerRepository>());
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<ICatalogSeriesRepository>(Mocker.Resolve<CatalogSeriesRepository>());
            Mocker.SetConstant<ICatalogEpisodeRepository>(Mocker.Resolve<CatalogEpisodeRepository>());
            Mocker.SetConstant<ICatalogEpisodeFileRepository>(Mocker.Resolve<CatalogEpisodeFileRepository>());
            Mocker.SetConstant<ICatalogMovieRepository>(Mocker.Resolve<CatalogMovieRepository>());
            Mocker.SetConstant<ICatalogMovieFileRepository>(Mocker.Resolve<CatalogMovieFileRepository>());
            Mocker.SetConstant<ICatalogArtistRepository>(Mocker.Resolve<CatalogArtistRepository>());
            Mocker.SetConstant<ICatalogAlbumRepository>(Mocker.Resolve<CatalogAlbumRepository>());
            Mocker.SetConstant<ICatalogTrackFileRepository>(Mocker.Resolve<CatalogTrackFileRepository>());
            Mocker.SetConstant<IFileHashRepository>(Mocker.Resolve<FileHashRepository>());

            Mocker.GetMock<IApplicationFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<ApplicationDefinition> { new() { Id = 3, Name = "Lidarr", Implementation = "Lidarr" } });

            var artists = Mocker.Resolve<ICatalogArtistRepository>();
            var albums = Mocker.Resolve<ICatalogAlbumRepository>();
            var tracks = Mocker.Resolve<ICatalogTrackFileRepository>();

            artists.Insert(new CatalogArtist { ApplicationId = 3, ArrId = 7, ForeignArtistId = "low-mbid", Name = "Low", Path = "/music/Low", QualityProfileId = 1 });
            artists.Insert(new CatalogArtist { ApplicationId = 3, ArrId = 8, ForeignArtistId = "slint-mbid", Name = "Slint", Path = "/music/Slint", QualityProfileId = 1 });

            // Low: one album held whole, in two tracks, and one Lidarr holds only one track of.
            albums.Insert(new CatalogAlbum { ApplicationId = 3, ArrId = 70, ArrArtistId = 7, ForeignAlbumId = "things-we-lost", Title = "Things We Lost in the Fire", Year = 2001, ReleaseDate = "2001-01-29", AlbumType = "Album", TrackCount = 2, TrackFileCount = 2 });
            albums.Insert(new CatalogAlbum { ApplicationId = 3, ArrId = 71, ArrArtistId = 7, ForeignAlbumId = "secret-name", Title = "Secret Name", Year = 1999, TrackCount = 2, TrackFileCount = 1 });
            albums.Insert(new CatalogAlbum { ApplicationId = 3, ArrId = 80, ArrArtistId = 8, ForeignAlbumId = "spiderland", Title = "Spiderland", Year = 1991, TrackCount = 1, TrackFileCount = 1 });

            tracks.Insert(new CatalogTrackFile { ApplicationId = 3, ArrId = 702, ArrArtistId = 7, ArrAlbumId = 70, Path = "/music/Low/Things We Lost in the Fire (2001)/02 - Dinosaur Act.flac", RelativePath = "Things We Lost in the Fire (2001)/02 - Dinosaur Act.flac", Size = 20, Quality = "FLAC", SceneName = "Low-Things_We_Lost_In_The_Fire-CD-FLAC-2001-GROUP" });
            tracks.Insert(new CatalogTrackFile { ApplicationId = 3, ArrId = 701, ArrArtistId = 7, ArrAlbumId = 70, Path = "/music/Low/Things We Lost in the Fire (2001)/01 - Sunflower.flac", RelativePath = "Things We Lost in the Fire (2001)/01 - Sunflower.flac", Size = 10, Quality = "FLAC", SceneName = "Low-Things_We_Lost_In_The_Fire-CD-FLAC-2001-GROUP" });
            tracks.Insert(new CatalogTrackFile { ApplicationId = 3, ArrId = 711, ArrArtistId = 7, ArrAlbumId = 71, Path = "/music/Low/Secret Name (1999)/01 - I Remember.mp3", RelativePath = "Secret Name (1999)/01 - I Remember.mp3", Size = 5, Quality = "MP3-320" });
            tracks.Insert(new CatalogTrackFile { ApplicationId = 3, ArrId = 801, ArrArtistId = 8, ArrAlbumId = 80, Path = "/music/Slint/Spiderland (1991)/01 - Breadcrumb Trail.flac", RelativePath = "Spiderland (1991)/01 - Breadcrumb Trail.flac", Size = 30, Quality = "FLAC" });

            _bob = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "bob", Name = "Bob", State = PeerState.Trusted, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });
        }

        private Peer Bob => Mocker.Resolve<IPeerRepository>().Get(_bob.Id);

        private void GivenShared(params ShareKey[] keys)
        {
            var list = Subject.Create("Music");

            Subject.Update(list.List.Id, null, keys, new[] { _bob.Id });
        }

        [Test]
        public void an_artist_should_be_offered_with_its_whole_albums_only()
        {
            GivenShared(Low);

            Subject.BuildOffer(Bob, true).Items.Select(i => i.Id).Should().BeEquivalentTo("a-3-70");
        }

        [Test]
        public void an_album_should_say_what_it_is_and_list_its_tracks_in_order_without_their_names()
        {
            GivenShared(Low);

            var album = Subject.BuildOffer(Bob, true).Items.Single();

            album.Kind.Should().Be(MediaKind.Music);
            album.Artist.Should().Be("Low");
            album.Title.Should().Be("Things We Lost in the Fire");
            album.Year.Should().Be(2001);
            album.ReleaseDate.Should().Be("2001-01-29");
            album.ForeignArtistId.Should().Be("low-mbid");
            album.ForeignAlbumId.Should().Be("things-we-lost");
            album.Quality.Should().Be("FLAC");
            album.Size.Should().Be(30);
            album.SceneName.Should().Be("Low-Things_We_Lost_In_The_Fire-CD-FLAC-2001-GROUP");
            album.Extension.Should().BeNull();
            album.Tracks.Select(t => t.Id).Should().Equal("t-3-701", "t-3-702");
            album.Tracks.Should().OnlyContain(t => t.Extension == ".flac");

            var json = System.Text.Json.JsonSerializer.Serialize(album);

            json.Should().NotContain("Sunflower").And.NotContain("/music");
        }

        [Test]
        public void a_node_that_does_not_know_albums_should_be_offered_none()
        {
            GivenShared(Low);

            Subject.BuildOffer(Bob, false).Items.Should().BeEmpty();
        }

        [Test]
        public void share_everything_should_offer_every_whole_album()
        {
            Subject.SetShareAll(_bob.Id, true);

            Subject.BuildOffer(Bob, true).Items.Select(i => i.Id).Should().BeEquivalentTo("a-3-70", "a-3-80");
        }

        [Test]
        public void a_hidden_artist_should_be_offered_to_nobody()
        {
            Subject.SetShareAll(_bob.Id, true);
            Subject.Update(Subject.All().Single(l => l.List.IsHidden).List.Id, null, new[] { Slint }, null);

            Subject.BuildOffer(Bob, true).Items.Select(i => i.Id).Should().BeEquivalentTo("a-3-70");
        }

        [Test]
        public void a_track_should_count_as_offered_only_while_its_album_is()
        {
            GivenShared(Low);

            Subject.IsOffered(Bob, "t-3-701").Should().BeTrue();
            Subject.IsOffered(Bob, "t-3-711").Should().BeFalse("its album is not whole");
            Subject.IsOffered(Bob, "t-3-801").Should().BeFalse("its artist is not shared");
            Subject.IsOffered(Bob, "a-3-70").Should().BeFalse("an album is not a file");
        }

        [Test]
        public void an_artist_should_be_listed_as_something_to_share()
        {
            var low = Subject.GetTitles().Single(t => t.Title == "Low");

            low.Key.Should().Be(new TitleKey(MediaKind.Music, 3, 7));
            low.Files.Should().Be(3);
        }

        [Test]
        public void an_album_should_not_be_put_on_a_list_on_its_own()
        {
            var list = Subject.Create("Music");

            Subject.Update(list.List.Id, null, new[] { new ShareKey(MediaKind.Music, 3, 7, 1, 70) }, null);

            Subject.All().Single(l => l.List.Id == list.List.Id).Items.Should().Equal(Low);
        }
    }
}

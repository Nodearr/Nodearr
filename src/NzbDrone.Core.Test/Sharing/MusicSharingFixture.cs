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
        public void an_album_poster_should_be_given_only_for_a_whole_album_shared()
        {
            GivenShared(Low);

            Subject.PosterFor(Bob, "a-3-70").Should().Be(new PosterTitle(3, PosterSubject.Album, 70));
            Subject.PosterFor(Bob, "a-3-71").Should().BeNull();
            Subject.PosterFor(Bob, "a-3-80").Should().BeNull();
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
        public void shared_details_should_include_only_offered_album_tracks_and_no_library_paths()
        {
            var artist = Mocker.Resolve<ICatalogArtistRepository>().ForArrIds(3, new[] { 7 }).Single();
            artist.Overview = "Biography";
            artist.Genres = "Ambient";
            Mocker.Resolve<ICatalogArtistRepository>().Update(artist);
            var album = Mocker.Resolve<ICatalogAlbumRepository>().ForArrIds(3, new[] { 70 }).Single();
            album.Overview = "Album description";
            album.Tracks = System.Text.Json.JsonSerializer.Serialize(new[] { new CatalogAlbumTrack { TrackFileId = 701, Duration = 120000 } });
            Mocker.Resolve<ICatalogAlbumRepository>().Update(album);
            var file = Mocker.Resolve<ICatalogTrackFileRepository>().ForArrIds(3, new[] { 701 }).Single();
            file.ForeignRecordingId = "11111111-1111-1111-1111-111111111111";
            Mocker.Resolve<ICatalogTrackFileRepository>().Update(file);
            GivenShared(Low);

            var offer = Subject.BuildOffer(Bob, true).Items.Single();
            offer.ArtistOverview.Should().Be("Biography");
            offer.ArtistGenres.Should().Be("Ambient");
            offer.Overview.Should().Be("Album description");
            offer.Tracks.Single(t => t.Id == "t-3-701").Duration.Should().Be(120000);
            offer.Tracks.Single(t => t.Id == "t-3-701").ForeignRecordingId.Should().Be(file.ForeignRecordingId);
            System.Text.Json.JsonSerializer.Serialize(offer).Should().NotContain("/music").And.NotContain("Secret Name");
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
        public void one_album_should_be_shared_on_its_own()
        {
            GivenShared(ShareKey.Album(3, 7, 70));

            Subject.BuildOffer(Bob, true).Items.Select(i => i.Id).Should().Equal("a-3-70");
            Subject.IsOffered(Bob, "t-3-701").Should().BeTrue();
            Subject.IsOffered(Bob, "t-3-801").Should().BeFalse();
        }

        [Test]
        public void an_album_that_is_not_whole_should_not_be_offered_even_on_its_own()
        {
            GivenShared(ShareKey.Album(3, 7, 71));

            Subject.BuildOffer(Bob, true).Items.Should().BeEmpty();
        }

        [Test]
        public void a_whole_artist_should_take_in_an_album_that_arrives_later()
        {
            GivenShared(Slint);

            Mocker.Resolve<ICatalogAlbumRepository>().Insert(new CatalogAlbum { ApplicationId = 3, ArrId = 81, ArrArtistId = 8, ForeignAlbumId = "tweez", Title = "Tweez", Year = 1989, TrackCount = 1, TrackFileCount = 1 });
            Mocker.Resolve<ICatalogTrackFileRepository>().Insert(new CatalogTrackFile { ApplicationId = 3, ArrId = 811, ArrArtistId = 8, ArrAlbumId = 81, Path = "/music/Slint/Tweez (1989)/01 - Ron.flac", Size = 10, Quality = "FLAC" });

            Subject.BuildOffer(Bob, true).Items.Select(i => i.Id).Should().BeEquivalentTo("a-3-80", "a-3-81");
        }

        [Test]
        public void hiding_one_album_should_keep_back_that_album_of_a_shared_artist_and_nothing_else()
        {
            Mocker.Resolve<ICatalogAlbumRepository>().Insert(new CatalogAlbum { ApplicationId = 3, ArrId = 81, ArrArtistId = 8, ForeignAlbumId = "tweez", Title = "Tweez", Year = 1989, TrackCount = 1, TrackFileCount = 1 });
            Mocker.Resolve<ICatalogTrackFileRepository>().Insert(new CatalogTrackFile { ApplicationId = 3, ArrId = 811, ArrArtistId = 8, ArrAlbumId = 81, Path = "/music/Slint/Tweez (1989)/01 - Ron.flac", Size = 10, Quality = "FLAC" });

            GivenShared(Slint);
            Subject.Update(Subject.All().Single(l => l.List.IsHidden).List.Id, null, new[] { ShareKey.Album(3, 8, 81) }, null);

            Subject.BuildOffer(Bob, true).Items.Select(i => i.Id).Should().Equal("a-3-80");
            Subject.IsOffered(Bob, "t-3-811").Should().BeFalse();
        }

        [Test]
        public void a_list_should_keep_an_album_only_where_its_whole_artist_is_not_on_it()
        {
            var list = Subject.Create("Music");

            Subject.Update(list.List.Id, null, new[] { ShareKey.Album(3, 7, 70), Low, ShareKey.Album(3, 8, 80) }, null);

            Subject.All().Single(l => l.List.Id == list.List.Id).Items.Should().BeEquivalentTo(new[] { Low, ShareKey.Album(3, 8, 80) });
        }

        [Test]
        public void the_albums_of_an_artist_should_list_which_can_be_offered()
        {
            var albums = Subject.GetAlbums(3, 7);

            albums.Select(a => a.AlbumArrId).Should().BeEquivalentTo(new[] { 70, 71 });
            albums.Single(a => a.AlbumArrId == 70).IsWhole.Should().BeTrue();
            albums.Single(a => a.AlbumArrId == 71).IsWhole.Should().BeFalse();
        }

        [Test]
        public void a_track_should_say_which_track_it_is_and_the_album_be_in_lidarrs_order()
        {
            var tracks = Mocker.Resolve<ICatalogTrackFileRepository>();

            // Lidarr numbers Dinosaur Act first, whatever the files are called.
            foreach (var (id, title, number) in new[] { (701, "Sunflower", 2), (702, "Dinosaur Act", 1) })
            {
                var file = tracks.ForArrIds(3, new[] { id }).Single();

                file.TrackTitle = title;
                file.TrackNumber = number;
                file.DiscNumber = 1;
                tracks.Update(file);
            }

            GivenShared(Low);

            var album = Subject.BuildOffer(Bob, true).Items.Single();

            album.Tracks.Select(t => t.Id).Should().Equal("t-3-702", "t-3-701");
            album.Tracks[0].Should().Match<OfferTrack>(t => t.Title == "Dinosaur Act" && t.Number == 1 && t.Disc == 1);
        }

        [Test]
        public void a_track_lidarr_has_not_matched_should_say_nothing_of_which_it_is()
        {
            GivenShared(Low);

            var json = System.Text.Json.JsonSerializer.Serialize(Subject.BuildOffer(Bob, true).Items.Single().Tracks[0]);

            json.Should().NotContain("Title").And.NotContain("Number").And.NotContain("Disc");
        }
    }
}

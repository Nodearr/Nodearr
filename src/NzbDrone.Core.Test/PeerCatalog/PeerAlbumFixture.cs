using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Test.PeerCatalog
{
    // A peer's album as this node keeps it, and how it compares with the local Lidarr (ADR 0020).
    [TestFixture]
    public class PeerAlbumFixture
    {
        private List<CatalogArtist> _artists;
        private List<CatalogAlbum> _albums;
        private List<CatalogTrackFile> _trackFiles;
        private List<CatalogQualityProfile> _profiles;

        [SetUp]
        public void Setup()
        {
            // Locally: Low, with one album whole in MP3-320, one with no tracks yet, and one with
            // half its tracks. The profile ranks MP3-320 below FLAC.
            _artists = new List<CatalogArtist> { new() { ApplicationId = 3, ArrId = 7, Name = "Low", QualityProfileId = 1 } };
            _albums = new List<CatalogAlbum>
            {
                new() { ApplicationId = 3, ArrId = 70, ArrArtistId = 7, ForeignAlbumId = "things-we-lost", TrackCount = 2, TrackFileCount = 2 },
                new() { ApplicationId = 3, ArrId = 71, ArrArtistId = 7, ForeignAlbumId = "secret-name", TrackCount = 2, TrackFileCount = 0 },
                new() { ApplicationId = 3, ArrId = 72, ArrArtistId = 7, ForeignAlbumId = "trust", TrackCount = 2, TrackFileCount = 1 }
            };
            _trackFiles = new List<CatalogTrackFile>
            {
                new() { ApplicationId = 3, ArrId = 701, ArrArtistId = 7, ArrAlbumId = 70, Quality = "MP3-320" },
                new() { ApplicationId = 3, ArrId = 702, ArrArtistId = 7, ArrAlbumId = 70, Quality = "MP3-320" },
                new() { ApplicationId = 3, ArrId = 721, ArrArtistId = 7, ArrAlbumId = 72, Quality = "MP3-320" }
            };

            _profiles = new List<CatalogQualityProfile> { new() { ApplicationId = 3, ArrId = 1, QualityRanking = Ranking().ToStored() } };
        }

        [Test]
        public void new_music_metadata_should_be_optional_and_bounded_without_discarding_the_album()
        {
            var offer = Offer("things-we-lost");
            offer.ArtistOverview = new string('x', 4001);
            offer.ArtistGenres = "Ambient";
            offer.Tracks[0].Duration = -1;
            offer.Tracks[0].ForeignRecordingId = "not-an-id";
            offer.Tracks[1].Duration = 123000;
            offer.Tracks[1].ForeignRecordingId = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";

            var item = PeerCatalogItem.FromOffer(1, offer);
            item.IsAlbum.Should().BeTrue();
            item.ArtistOverview.Should().BeNull();
            item.ArtistGenres.Should().Be("Ambient");
            item.GetTracks()[0].Duration.Should().BeNull();
            item.GetTracks()[0].ForeignRecordingId.Should().BeNull();
            item.GetTracks()[1].Duration.Should().Be(123000);
            item.GetTracks()[1].ForeignRecordingId.Should().Be("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        }

        private PeerItemStatus Classify(PeerCatalogItem item)
        {
            return new LocalHoldings(new List<CatalogSeries>(), new List<CatalogEpisode>(), new List<CatalogEpisodeFile>(), new List<CatalogMovie>(), new List<CatalogMovieFile>(), _profiles, _artists, _albums, _trackFiles).Classify(item);
        }

        private static OfferItem Offer(string foreignAlbumId, string quality = "FLAC", List<OfferTrack> tracks = null)
        {
            return new OfferItem
            {
                Id = "a-3-70",
                Kind = MediaKind.Music,
                Artist = "Low",
                Title = "Things We Lost in the Fire",
                Year = 2001,
                ForeignArtistId = "low-mbid",
                ForeignAlbumId = foreignAlbumId,
                Quality = quality,
                Tracks = tracks ?? new List<OfferTrack>
                {
                    new() { Id = "t-3-701", Extension = ".flac", Size = 10, Quality = quality },
                    new() { Id = "t-3-702", Extension = ".flac", Size = 20, Quality = quality }
                }
            };
        }

        private static PeerCatalogItem Album(string foreignAlbumId, string quality = "FLAC")
        {
            return PeerCatalogItem.FromOffer(1, Offer(foreignAlbumId, quality));
        }

        [Test]
        public void an_album_should_be_kept_with_its_artist_ids_and_tracks()
        {
            var item = Album("THINGS-WE-LOST");

            item.IsAlbum.Should().BeTrue();
            item.Artist.Should().Be("Low");
            item.ForeignAlbumId.Should().Be("things-we-lost");
            item.GetTracks().Should().HaveCount(2);
            item.GetTracks()[1].Id.Should().Be("t-3-702");
        }

        [TestCase(".FLAC")]
        [TestCase("flac")]
        [TestCase(".fl/ac")]
        [TestCase(null)]
        public void an_album_whose_tracks_would_not_do_should_be_kept_but_never_used(string extension)
        {
            var item = PeerCatalogItem.FromOffer(1, Offer("things-we-lost", tracks: new List<OfferTrack> { new() { Id = "t-3-701", Extension = extension, Size = 10 } }));

            item.IsAlbum.Should().BeFalse();
            item.RemoteId.Should().Be("a-3-70");
            Classify(item).Should().Be(PeerItemStatus.NotInLibrary);
        }

        [Test]
        public void an_album_with_the_same_track_twice_should_never_be_used()
        {
            var track = new OfferTrack { Id = "t-3-701", Extension = ".flac", Size = 10 };

            PeerCatalogItem.FromOffer(1, Offer("things-we-lost", tracks: new List<OfferTrack> { track, track })).IsAlbum.Should().BeFalse();
        }

        [Test]
        public void a_kind_of_file_from_a_newer_node_should_be_not_in_library()
        {
            Classify(new PeerCatalogItem { MediaKind = (MediaKind)9, ForeignAlbumId = "things-we-lost" }).Should().Be(PeerItemStatus.NotInLibrary);
        }

        [Test]
        public void an_album_lidarr_does_not_have_should_be_not_in_library()
        {
            Classify(Album("spiderland")).Should().Be(PeerItemStatus.NotInLibrary);
        }

        [Test]
        public void an_album_without_tracks_here_should_be_missing()
        {
            Classify(Album("secret-name")).Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void an_album_held_only_in_part_should_be_missing()
        {
            Classify(Album("trust")).Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void a_better_album_should_be_an_upgrade()
        {
            Classify(Album("things-we-lost", "FLAC")).Should().Be(PeerItemStatus.Upgrade);
        }

        [Test]
        public void the_same_album_at_the_same_quality_should_be_had()
        {
            Classify(Album("things-we-lost", "MP3-320")).Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void an_album_should_be_titled_the_way_lidarr_names_its_folders()
        {
            var item = Album("things-we-lost", "MP3-320");

            ReleaseTitleBuilder.For(item).Should().Be("Low - Things We Lost in the Fire (2001) [MP3 320]");
        }

        [Test]
        public void an_album_should_be_titled_by_its_release_name_where_lidarr_kept_one()
        {
            var item = Album("things-we-lost");

            item.SceneName = "Low-Things_We_Lost_In_The_Fire-CD-FLAC-2001-GROUP";

            ReleaseTitleBuilder.For(item).Should().Be("Low-Things_We_Lost_In_The_Fire-CD-FLAC-2001-GROUP");
        }

        [Test]
        public void an_album_title_should_hold_nothing_a_folder_name_cannot()
        {
            var item = Album("things-we-lost");

            item.Artist = "AC/DC";
            item.Title = "Who Made Who: [Live]";

            ReleaseTitleBuilder.For(item).Should().Be("AC DC - Who Made Who Live (2001) [FLAC]");
        }

        [Test]
        public void a_tracks_title_and_place_should_be_kept()
        {
            var item = PeerCatalogItem.FromOffer(1, Offer("things-we-lost", tracks: new List<OfferTrack> { new() { Id = "t-3-701", Extension = ".flac", Size = 10, Title = " Sunflower ", Number = 1, Disc = 1 } }));

            item.GetTracks().Single().Should().Match<OfferTrack>(t => t.Title == "Sunflower" && t.Number == 1 && t.Disc == 1);
        }

        [Test]
        public void a_title_or_number_that_would_not_do_should_be_dropped_and_the_album_kept()
        {
            var tracks = new List<OfferTrack>
            {
                new() { Id = "t-3-701", Extension = ".flac", Size = 10, Title = "Sun\u0007flower", Number = -1, Disc = 500 },
                new() { Id = "t-3-702", Extension = ".flac", Size = 10, Title = new string('x', 301), Number = 10000 }
            };

            var item = PeerCatalogItem.FromOffer(1, Offer("things-we-lost", tracks: tracks));

            item.IsAlbum.Should().BeTrue();
            item.GetTracks().Should().OnlyContain(t => t.Title == null && t.Number == null && t.Disc == null);
        }

        // Lidarr's profile: MP3-320, then FLAC, upgrades on, cutoff FLAC.
        private static QualityRanking Ranking()
        {
            return new QualityRanking
            {
                UpgradeAllowed = true,
                CutoffRank = 1,
                Qualities = new List<RankedQuality>
                {
                    new() { Name = "MP3-320", Rank = 0, Allowed = true },
                    new() { Name = "FLAC", Rank = 1, Allowed = true }
                }
            };
        }
    }
}

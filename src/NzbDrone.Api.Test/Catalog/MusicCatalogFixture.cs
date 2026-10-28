using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Nodearr.Api.V5.Catalog;
using Nodearr.Http;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Catalog
{
    [TestFixture]
    public class MusicCatalogFixture : TestBase<MusicCatalogController>
    {
        private List<CatalogArtist> _artists;
        private List<CatalogAlbum> _albums;
        private List<CatalogTrackFile> _files;
        private List<PeerCatalogItem> _offers;

        [SetUp]
        public void Setup()
        {
            _artists = new() { new() { ApplicationId = 1, ArrId = 7, Name = "Artist", ForeignArtistId = "artist-id", Overview = "Biography", Genres = "Ambient", QualityProfileId = 1 } };
            _albums = new()
            {
                new()
                {
                    ApplicationId = 1, ArrId = 70, ArrArtistId = 7, Title = "First", ForeignAlbumId = "first-id",
                    TrackCount = 2, TrackFileCount = 1, Overview = "First description",
                    Tracks = JsonSerializer.Serialize(new List<CatalogAlbumTrack>
                    {
                        new() { Title = "One", Number = 1, Disc = 1, TrackFileId = 701, Duration = 123000 },
                        new() { Title = "Two", Number = 2, Disc = 1 }
                    })
                },
                new() { ApplicationId = 1, ArrId = 71, ArrArtistId = 7, Title = "Second", ForeignAlbumId = "second-id", TrackCount = 1, TrackFileCount = 1 }
            };
            _files = new()
            {
                new() { ApplicationId = 1, ArrId = 701, ArrAlbumId = 70, ArrArtistId = 7, Quality = "MP3-320", Size = 10, ForeignRecordingId = "11111111-1111-1111-1111-111111111111" },
                new() { ApplicationId = 1, ArrId = 702, ArrAlbumId = 71, ArrArtistId = 7, Quality = "MP3-320", Size = 10 }
            };
            var definition = new ApplicationDefinition { Id = 1, Name = "Lidarr", Implementation = "Lidarr" };
            Mocker.GetMock<IApplicationFactory>().Setup(a => a.All()).Returns(new List<ApplicationDefinition> { definition });
            Mocker.GetMock<IApplicationFactory>().Setup(a => a.Find(1)).Returns(definition);
            Mocker.GetMock<ICatalogArtistRepository>().Setup(r => r.All()).Returns(_artists);
            Mocker.GetMock<ICatalogAlbumRepository>().Setup(r => r.All()).Returns(_albums);
            Mocker.GetMock<ICatalogTrackFileRepository>().Setup(r => r.All()).Returns(_files);
            Mocker.GetMock<ICatalogArtistRepository>().Setup(r => r.ForArrIds(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<int, IEnumerable<int>>((app, ids) => _artists.Where(a => a.ApplicationId == app && ids.Contains(a.ArrId)).ToList());
            Mocker.GetMock<ICatalogAlbumRepository>().Setup(r => r.ForArrIds(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<int, IEnumerable<int>>((app, ids) => _albums.Where(a => a.ApplicationId == app && ids.Contains(a.ArrId)).ToList());
            Mocker.GetMock<ICatalogAlbumRepository>().Setup(r => r.ForApplication(It.IsAny<int>())).Returns<int>(app => _albums.Where(a => a.ApplicationId == app).ToList());
            Mocker.GetMock<ICatalogTrackFileRepository>().Setup(r => r.ForApplication(It.IsAny<int>())).Returns<int>(app => _files.Where(a => a.ApplicationId == app).ToList());
            var profile = new ArrQualityProfileResource
            {
                Id = 1, UpgradeAllowed = true, Cutoff = 2, Items = new()
                {
                    new() { Allowed = true, Quality = new ArrQualityResource { Id = 1, Name = "MP3-320" } },
                    new() { Allowed = true, Quality = new ArrQualityResource { Id = 2, Name = "FLAC" } }
                }
            }.ToCatalog();
            profile.ApplicationId = 1;
            Mocker.GetMock<ILocalHoldingsFactory>().Setup(f => f.Load()).Returns(() => new LocalHoldings(
                Array.Empty<CatalogSeries>(),
                Array.Empty<CatalogEpisode>(),
                Array.Empty<CatalogEpisodeFile>(),
                Array.Empty<CatalogMovie>(),
                Array.Empty<CatalogMovieFile>(),
                new[] { profile },
                _artists,
                _albums,
                _files));
            _offers = new() { Offer(3, 10, "first-id"), Offer(3, 11, "second-id"), Offer(9, 12, "private-to-another-friend") };
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetMusic()).Returns(() => _offers);
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetSummaries()).Returns(new List<PeerCatalogSummary>
            {
                new() { PeerId = 3, PeerName = "Alice" }, new() { PeerId = 9, PeerName = "Bob" }
            });
        }

        private static PeerCatalogItem Offer(int peer, int id, string album)
        {
            var offer = PeerCatalogItem.FromOffer(peer, new OfferItem
            {
                Id = $"a-1-{id}", Kind = MediaKind.Music, Artist = "Artist", ForeignArtistId = "artist-id", Title = album, ForeignAlbumId = album,
                ArtistOverview = "Shared biography", ArtistGenres = "Ambient", Overview = "Shared album", Quality = "FLAC",
                Tracks = new()
                {
                    new() { Id = $"t-1-{id}1", Title = "One", Number = 1, Disc = 1, Size = 10, Quality = "FLAC", Extension = ".flac", ForeignRecordingId = "11111111-1111-1111-1111-111111111111" },
                    new() { Id = $"t-1-{id}2", Title = "Two", Number = 2, Disc = 1, Size = 10, Quality = "FLAC", Extension = ".flac", ForeignRecordingId = "22222222-2222-2222-2222-222222222222" }
                }
            });
            offer.Id = id;
            return offer;
        }

        private CatalogDetailsResource Local(string kind, int id) => Subject.LocalDetails(kind, 1, id).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;
        private CatalogDetailsResource Peer(string kind, int id) => Subject.PeerDetails(kind, id).Result.Should().BeOfType<Ok<CatalogDetailsResource>>().Subject.Value;

        [Test]
        public void local_artist_should_show_albums_and_keep_artist_and_album_sends_distinct()
        {
            var artist = Local("artist", 7);
            artist.Overview.Should().Be("Biography");
            artist.Albums.Should().HaveCount(2);
            artist.Albums.Should().OnlyContain(a => a.PosterPath.Contains("/album/"));
            artist.Send.AlbumArrId.Should().BeNull();
            artist.Albums[0].Send.Should().BeNull();
            artist.Albums[1].Send.AlbumArrId.Should().Be(71);
            Local("album", 71).Send.AlbumArrId.Should().Be(71);
        }

        [Test]
        public void incomplete_local_album_should_show_missing_songs_and_never_offer_send()
        {
            var album = Local("album", 70);
            album.ParentPath.Should().Be("/catalog/music/local/artist/1/7");
            album.Tracks.Select(t => t.HasFile).Should().Equal(true, false);
            album.Tracks[0].Duration.Should().Be(123000);
            album.Send.Should().BeNull();
            album.LocalStatus.Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void stale_files_should_not_count_as_downloaded_or_sendable()
        {
            _files[0].MissingSince = DateTime.UtcNow;
            Local("album", 70).Tracks.Should().OnlyContain(t => !t.HasFile);
            _albums[1].MissingSince = DateTime.UtcNow;
            Local("artist", 7).Albums.Should().ContainSingle();
            Subject.LocalDetails("album", 1, 71).Result.Should().BeOfType<NotFound>();
        }

        [TestCase("track", 70)]
        [TestCase("album", 999)]
        [TestCase("artist", 999)]
        public void invalid_local_selection_should_not_fall_back_to_another_entity(string kind, int id)
        {
            Subject.LocalDetails(kind, 1, id).Result.Should().BeOfType<NotFound>();
            Subject.LocalDetails("album", 2, 70).Result.Should().BeOfType<NotFound>();
        }

        [Test]
        public void both_local_levels_should_use_the_same_paged_entries_and_details_actions()
        {
            var first = Subject.Local(new PagingRequestResource { PageSize = 1 }, "album").Value;
            var second = Subject.Local(new PagingRequestResource { Page = 2, PageSize = 1 }, "album").Value;
            first.TotalRecords.Should().Be(2);
            first.Records.Single().Key.Should().NotBe(second.Records.Single().Key);
            Subject.Local(new PagingRequestResource(), "artist").Value.Records.Single().AlbumCount.Should().Be(2);
        }

        [Test]
        public void peer_artist_should_include_only_that_senders_current_albums_and_use_artist_scope()
        {
            var artist = Peer("artist", 10);
            artist.Albums.Should().HaveCount(2).And.NotContain(a => a.Title == "private-to-another-friend");
            artist.GetIt.Scope.Should().Be(CatalogGetItScope.Artist);
            artist.Overview.Should().Be("Shared biography");
            Peer("album", 10).GetIt.Scope.Should().Be(CatalogGetItScope.File);
            Mocker.GetMock<IApplicationFactory>().Verify(a => a.GetInstance(It.IsAny<ApplicationDefinition>()), Times.Never());
        }

        [Test]
        public void peer_songs_should_distinguish_an_upgrade_from_a_missing_recording()
        {
            var album = Peer("album", 10);
            album.Tracks.Select(t => t.LocalStatus).Should().Equal(PeerItemStatus.Upgrade, PeerItemStatus.Missing);
            album.Tracks.Should().OnlyContain(t => t.HasFile);
            album.LocalStatus.Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void older_offers_should_not_guess_song_availability_from_titles_or_positions()
        {
            var tracks = _offers[0].GetTracks();
            tracks.ForEach(t => t.ForeignRecordingId = null);
            _offers[0].Tracks = JsonSerializer.Serialize(tracks);
            Peer("album", 10).Tracks.Should().OnlyContain(t => t.LocalStatus == null);
        }

        [Test]
        public void withdrawn_or_untrusted_music_cannot_reopen_cached_cards()
        {
            _offers.RemoveAt(0);
            Subject.PeerDetails("album", 10).Result.Should().BeOfType<NotFound>();
            Mocker.GetMock<IPeerCatalogService>().Setup(s => s.GetSummaries()).Returns(new List<PeerCatalogSummary>());
            Subject.PeerDetails("artist", 11).Result.Should().BeOfType<NotFound>();
        }

        [Test]
        public void peer_root_should_keep_friends_separate_and_apply_status_to_albums()
        {
            Subject.Peer(new PagingRequestResource()).Value.Records.Should().HaveCount(2);
            var missing = Subject.Peer(new PagingRequestResource(), "album", PeerItemStatus.Missing).Value;
            missing.Records.Should().ContainSingle().Which.ForeignAlbumId.Should().Be("first-id");
        }
    }
}

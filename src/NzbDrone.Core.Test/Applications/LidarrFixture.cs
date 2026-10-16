using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Trimmed from what Lidarr's v1 API sends.
    [TestFixture]
    public class LidarrFixture : CoreTest
    {
        [Test]
        public void an_album_should_keep_its_release_group_and_release_day()
        {
            var album = JsonConvert.DeserializeObject<LidarrAlbumResource>(
                "{\"id\":70,\"artistId\":7,\"foreignAlbumId\":\"5b6f8a9c-0000-4000-8000-000000000001\",\"title\":\"Things We Lost in the Fire\",\"albumType\":\"Album\",\"releaseDate\":\"2001-01-29T00:00:00Z\",\"monitored\":true," +
                "\"statistics\":{\"trackFileCount\":12,\"trackCount\":12,\"totalTrackCount\":12}}");

            var catalog = album.ToCatalog();

            catalog.ArrId.Should().Be(70);
            catalog.ArrArtistId.Should().Be(7);
            catalog.ForeignAlbumId.Should().Be("5b6f8a9c-0000-4000-8000-000000000001");
            catalog.ReleaseDate.Should().Be("2001-01-29");
            catalog.Year.Should().Be(2001);
            catalog.IsWhole.Should().BeTrue();
        }

        [Test]
        public void a_track_file_should_have_its_path_below_the_artist_folder()
        {
            var file = JsonConvert.DeserializeObject<LidarrTrackFileResource>(
                "{\"id\":701,\"artistId\":7,\"albumId\":70,\"path\":\"/music/Low/Things We Lost in the Fire (2001)/01 - Sunflower.flac\",\"size\":30000000," +
                "\"quality\":{\"quality\":{\"id\":6,\"name\":\"FLAC\"}},\"mediaInfo\":{\"audioChannels\":2,\"audioCodec\":\"FLAC\"}}");

            var catalog = file.ToCatalog("/music/Low/");

            catalog.ArrAlbumId.Should().Be(70);
            catalog.RelativePath.Should().Be("Things We Lost in the Fire (2001)/01 - Sunflower.flac");
            catalog.Quality.Should().Be("FLAC");
            catalog.AudioChannels.Should().Be(2);
        }

        [Test]
        public void a_track_file_outside_the_artist_folder_should_keep_no_relative_path()
        {
            var file = new LidarrTrackFileResource { Id = 1, Path = "/elsewhere/01.mp3" };

            file.ToCatalog("/music/Low").RelativePath.Should().BeNull();
        }
    }
}

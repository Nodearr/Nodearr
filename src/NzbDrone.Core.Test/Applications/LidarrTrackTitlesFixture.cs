using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Lidarr;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Each track file is kept with the track Lidarr says it is, so a friend can name it.
    [TestFixture]
    public class LidarrTrackTitlesFixture : CoreTest<Lidarr>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ApplicationDefinition
            {
                Name = "Lidarr",
                Settings = new LidarrSettings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://lidarr:8686" }
            };

            var proxy = Mocker.GetMock<IArrV3Proxy>();

            proxy.Setup(p => p.Get<LidarrArtistResource>(It.IsAny<IArrConnection>(), "/artist/7"))
                 .Returns(new LidarrArtistResource { Id = 7, ArtistName = "Slint", Path = "/music/Slint" });
            proxy.Setup(p => p.Get<List<LidarrAlbumResource>>(It.IsAny<IArrConnection>(), "/album?artistId=7"))
                 .Returns(new List<LidarrAlbumResource> { new() { Id = 70, ArtistId = 7, Title = "Spiderland" } });
            proxy.Setup(p => p.Get<List<LidarrTrackResource>>(It.IsAny<IArrConnection>(), "/track?artistId=7"))
                 .Returns(new List<LidarrTrackResource>
                 {
                     new() { TrackFileId = 701, AbsoluteTrackNumber = 1, MediumNumber = 1, Title = "Breadcrumb Trail" },
                     new() { TrackFileId = 702, AbsoluteTrackNumber = 2, MediumNumber = 1, Title = "Nosferatu Man" },

                     // A track Lidarr has no file for.
                     new() { TrackFileId = 0, AbsoluteTrackNumber = 3, MediumNumber = 1, Title = "Don, Aman" }
                 });
            proxy.Setup(p => p.Get<List<LidarrTrackFileResource>>(It.IsAny<IArrConnection>(), "/trackfile?artistId=7"))
                 .Returns(new List<LidarrTrackFileResource>
                 {
                     new() { Id = 701, ArtistId = 7, AlbumId = 70, Path = "/music/Slint/Spiderland (1991)/01.flac", Size = 1 },
                     new() { Id = 702, ArtistId = 7, AlbumId = 70, Path = "/music/Slint/Spiderland (1991)/02.flac", Size = 1 },

                     // A file Lidarr has not matched to a track.
                     new() { Id = 709, ArtistId = 7, AlbumId = 70, Path = "/music/Slint/Spiderland (1991)/extra.flac", Size = 1 }
                 });
        }

        [Test]
        public void each_track_file_should_be_kept_with_its_title_number_and_disc()
        {
            var files = Subject.FetchTitle(7).TrackFiles.ToDictionary(f => f.ArrId);

            files[701].Should().Match<NzbDrone.Core.Catalog.CatalogTrackFile>(f => f.TrackTitle == "Breadcrumb Trail" && f.TrackNumber == 1 && f.DiscNumber == 1);
            files[702].TrackTitle.Should().Be("Nosferatu Man");
            files[709].Should().Match<NzbDrone.Core.Catalog.CatalogTrackFile>(f => f.TrackTitle == null && f.TrackNumber == 0);
        }
    }
}

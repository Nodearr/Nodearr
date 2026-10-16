using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Whisparr;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Whisparr v2 keeps scenes as a site's episodes. Nodearr keeps each as a scene, as v3 does.
    [TestFixture]
    public class WhisparrV2Fixture : CoreTest<ArrV3Proxy>
    {
        [Test]
        public void a_scene_should_be_kept_as_a_scene_with_its_site_as_the_studio()
        {
            var site = new WhisparrV2SiteResource { Id = 3, Title = "ATK Girlfriends", Path = "/sites/atk", QualityProfileId = 2, TvdbId = 77 };
            var scene = JsonConvert.DeserializeObject<WhisparrV2SceneResource>(
                "{\"id\":40,\"seriesId\":3,\"tvdbId\":9001,\"episodeFileId\":12,\"title\":\"Scene Title\",\"releaseDate\":\"2022-11-28\",\"monitored\":true," +
                "\"episodeFile\":{\"id\":12,\"relativePath\":\"a.mkv\",\"size\":5,\"quality\":{\"quality\":{\"name\":\"WEBDL-1080p\"}}}}");

            var movie = scene.ToCatalog(site);

            movie.ArrId.Should().Be(40);
            movie.IsAdult.Should().BeTrue();
            movie.ItemType.Should().Be("scene");
            movie.Studio.Should().Be("ATK Girlfriends");
            movie.ReleaseDate.Should().Be("2022-11-28");
            movie.Year.Should().Be(2022);
            movie.QualityProfileId.Should().Be(2);
            movie.ArrMovieFileId.Should().Be(12);
            movie.ForeignId.Should().Be("whisparr2:9001");
        }

        private void GivenStatus(string appName, string version)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), $"{{\"appName\":\"{appName}\",\"version\":\"{version}\"}}"));
        }

        private static ArrApplicationSettings Settings()
        {
            return new WhisparrV2Settings { ApiKey = "key" };
        }

        [Test]
        public void a_whisparr_v2_should_pass_the_test_as_v2()
        {
            GivenStatus("Whisparr", "2.2.0.404");

            Subject.TestConnection(Settings(), "Whisparr", new System.Version(2, 0, 0, 0), new System.Version(3, 0, 0, 0)).Should().BeNull();
        }

        [Test]
        public void a_whisparr_v3_should_be_told_to_be_added_as_v3()
        {
            GivenStatus("Whisparr", "3.6.5.1886");

            Subject.TestConnection(Settings(), "Whisparr", new System.Version(2, 0, 0, 0), new System.Version(3, 0, 0, 0))
                   .ErrorMessage.Should().Contain("Add it as Whisparr v3");
        }
    }
}

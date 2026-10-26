using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.Applications;

namespace NzbDrone.Core.Test.Applications
{
    [TestFixture]
    public class ArrCatalogDetailsFixture
    {
        [Test]
        public void sonarr_series_and_episode_should_keep_their_own_descriptions()
        {
            var series = JsonConvert.DeserializeObject<SonarrSeriesResource>(
                "{\"id\":10,\"title\":\"Pingu\",\"overview\":\"Series story\",\"genres\":[\"Family\",\"Comedy\"],\"network\":\"SRF\",\"runtime\":5}").ToCatalog();
            var episode = JsonConvert.DeserializeObject<SonarrEpisodeResource>(
                "{\"id\":11,\"seriesId\":10,\"title\":\"Episode story\",\"overview\":\"Episode description\"}").ToCatalog();

            series.Overview.Should().Be("Series story");
            series.Genres.Should().Be("Family, Comedy");
            series.Network.Should().Be("SRF");
            series.Runtime.Should().Be(5);
            episode.Overview.Should().Be("Episode description");
        }

        [TestCase("movie")]
        [TestCase("scene")]
        public void radarr_and_whisparr_should_keep_arr_descriptions(string itemType)
        {
            var radarr = JsonConvert.DeserializeObject<RadarrMovieResource>(
                "{\"id\":20,\"title\":\"Film\",\"overview\":\"Film story\",\"genres\":[\"Drama\"],\"runtime\":120}").ToCatalog();
            var whisparr = JsonConvert.DeserializeObject<WhisparrMovieResource>(
                "{\"id\":21,\"title\":\"A title\",\"itemType\":\"" + itemType + "\",\"overview\":\"Whisparr story\",\"studioTitle\":\"Studio\"}").ToCatalog();

            radarr.Overview.Should().Be("Film story");
            radarr.Genres.Should().Be("Drama");
            radarr.Runtime.Should().Be(120);
            whisparr.ItemType.Should().Be(itemType);
            whisparr.Overview.Should().Be("Whisparr story");
            whisparr.Studio.Should().Be("Studio");
        }
    }
}

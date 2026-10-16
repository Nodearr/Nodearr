using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Test.PeerIndexer
{
    // The text queries Whisparr v3 sends for a scene, as its ReleaseSearchService and Newznab
    // request generator write them, against a scene a peer offers.
    [TestFixture]
    public class SceneQueryFixture
    {
        private static PeerCatalogItem Scene()
        {
            return new PeerCatalogItem
            {
                MediaKind = MediaKind.Movie,
                IsAdult = true,
                ItemType = "scene",
                Studio = "Couch Casting-X",
                ReleaseDate = "2024-10-13",
                Title = "Café Rendez-vous & Don't Look Back",
                Code = "CCX-1182",
                Year = 2024
            };
        }

        [TestCase("Couch Casting X 24.10.13")]
        [TestCase("CouchCastingX 24.10.13")]
        [TestCase("Couch+Casting+X+13.10.2024")]
        [TestCase("Couch Casting X 241013")]
        [TestCase("Cafe Rendez vous and Dont Look Back 24.10.13")]
        [TestCase("Couch Casting X Cafe Rendez vous and Dont Look Back")]
        [TestCase("Couch Casting X CCX 1182")]
        [TestCase("Cafe Rendez vous and Dont Look Back")]
        public void each_way_whisparr_asks_for_the_scene_should_find_it(string query)
        {
            SceneQuery.Parse(query).Matches(Scene()).Should().BeTrue();
        }

        [TestCase("Couch Casting X 24.10.14")]
        [TestCase("Brazzers 24.10.13")]
        [TestCase("Couch Casting X Another Title")]
        public void another_scene_should_not_be_found(string query)
        {
            SceneQuery.Parse(query).Matches(Scene()).Should().BeFalse();
        }

        [Test]
        public void a_date_either_way_round_should_be_read_as_the_same_day()
        {
            SceneQuery.Parse("Studio 24.10.13").ReleaseDate.Should().Be("2024-10-13");
            SceneQuery.Parse("Studio 13.10.2024").ReleaseDate.Should().Be("2024-10-13");
            SceneQuery.Parse("Studio 2024.10.13").ReleaseDate.Should().Be("2024-10-13");
        }

        [Test]
        public void numbers_that_are_not_a_day_should_be_words()
        {
            var query = SceneQuery.Parse("Studio 24.13.40");

            query.ReleaseDate.Should().BeNull();
            query.Words.Should().Contain("24");
        }

        [Test]
        public void nothing_that_is_not_adult_content_should_be_found()
        {
            var movie = Scene();
            movie.IsAdult = false;
            movie.ItemType = null;

            SceneQuery.Parse("Couch Casting X 24.10.13").Matches(movie).Should().BeFalse();
        }

        [Test]
        public void an_empty_query_should_find_nothing()
        {
            SceneQuery.Parse("").Matches(Scene()).Should().BeFalse();
        }
    }
}

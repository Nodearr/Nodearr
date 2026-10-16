using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Test.PeerIndexer
{
    [TestFixture]
    public class AdultReleaseTitleFixture
    {
        private static PeerCatalogItem Scene(string sceneName = null)
        {
            return new PeerCatalogItem
            {
                MediaKind = MediaKind.Movie, IsAdult = true, ItemType = "scene", Studio = "Couch Casting-X", ReleaseDate = "2024-10-13",
                Title = "Scene Title", Quality = "WEBDL-1080p", SceneName = sceneName
            };
        }

        [Test]
        public void a_scene_should_be_named_as_scenes_are_released()
        {
            ReleaseTitleBuilder.For(Scene()).Should().Be("CouchCastingX.24.10.13.Scene.Title.XXX.1080p.WEB-DL");
        }

        [Test]
        public void a_scene_should_keep_its_own_release_name()
        {
            ReleaseTitleBuilder.For(Scene("CouchCastingX.24.10.13.Someone.Scene.Title.XXX.1080p.MP4-GROUP"))
                .Should().Be("CouchCastingX.24.10.13.Someone.Scene.Title.XXX.1080p.MP4-GROUP");
        }

        [Test]
        public void a_film_should_be_named_as_a_movie_marked_xxx()
        {
            var film = new PeerCatalogItem { MediaKind = MediaKind.Movie, IsAdult = true, ItemType = "movie", Title = "A Film", Year = 2020, Quality = "Bluray-1080p" };

            ReleaseTitleBuilder.For(film).Should().Be("A.Film.2020.XXX.1080p.BluRay");
        }

        [Test]
        public void radarrs_movies_should_be_named_as_they_were()
        {
            var movie = new PeerCatalogItem { MediaKind = MediaKind.Movie, Title = "A Movie", Year = 2020, Quality = "WEBDL-1080p" };

            ReleaseTitleBuilder.For(movie).Should().Be("A.Movie.2020.1080p.WEB-DL");
        }

        [TestCase("\"scene\"", "scene")]
        [TestCase("\"movie\"", "movie")]
        [TestCase("1", "scene")]
        [TestCase("0", "movie")]
        public void whisparrs_item_type_should_be_read_by_name_or_number(string json, string expected)
        {
            var resource = JsonConvert.DeserializeObject<WhisparrMovieResource>($"{{\"id\":7,\"title\":\"T\",\"itemType\":{json},\"releaseDate\":\"2024-10-13T00:00:00Z\",\"studioTitle\":\"S\",\"stashId\":\"abc\"}}");

            var movie = resource.ToCatalog();

            movie.ItemType.Should().Be(expected);
            movie.IsAdult.Should().BeTrue();
            movie.ReleaseDate.Should().Be("2024-10-13");
            movie.Studio.Should().Be("S");
            movie.StashId.Should().Be("abc");
        }

        [Test]
        public void an_offer_without_whisparrs_fields_should_carry_none_of_them()
        {
            var offer = Offer.Seal(new System.Collections.Generic.List<OfferItem> { new() { Id = "m-1-1", Kind = MediaKind.Movie, Title = "A Movie" } });

            System.Text.Json.JsonSerializer.Serialize(offer.Items[0]).Should().NotContain("Adult").And.NotContain("Studio").And.NotContain("ReleaseDate");
        }
    }
}

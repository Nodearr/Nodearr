using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PeerIndexer
{
    // Adult content from peers' Whisparr is found by an adult search, and by nothing else. Radarr
    // never sees it, and Whisparr never sees Radarr's movies.
    [TestFixture]
    public class AdultCandidateFixture : DbTest<CandidateService, PeerCatalogItem>
    {
        private const int SharedTmdbId = 4242;

        private Peer _alice;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());

            _alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });

            // A movie from Alice's Radarr, and a film and a scene from her Whisparr. The film shares
            // the movie's TMDb ID, to show the two are still kept apart.
            Db.Insert(new PeerCatalogItem
            {
                PeerId = _alice.Id, RemoteId = "radarr", CandidateId = "radarr", MediaKind = MediaKind.Movie, TmdbId = SharedTmdbId,
                Title = "A Movie", Year = 2020, Resolution = 1080, ReleaseTitle = "A.Movie.2020.1080p.WEB-DL", DateAdded = DateTime.UtcNow.AddDays(-3)
            });

            Db.Insert(new PeerCatalogItem
            {
                PeerId = _alice.Id, RemoteId = "film", CandidateId = "film", MediaKind = MediaKind.Movie, TmdbId = SharedTmdbId,
                IsAdult = true, ItemType = "movie", Title = "A Film", Year = 2020, Resolution = 1080,
                ReleaseTitle = "A.Film.2020.XXX.1080p.WEB-DL", DateAdded = DateTime.UtcNow.AddDays(-2)
            });

            Db.Insert(new PeerCatalogItem
            {
                PeerId = _alice.Id, RemoteId = "scene", CandidateId = "scene", MediaKind = MediaKind.Movie,
                IsAdult = true, ItemType = "scene", Studio = "Couch Casting-X", ReleaseDate = "2024-10-13", Title = "Scene Title", Code = "CCX-1182",
                Resolution = 1080, ReleaseTitle = "CouchCastingX.24.10.13.Scene.Title.XXX.1080p.WEB-DL", DateAdded = DateTime.UtcNow.AddDays(-1)
            });
        }

        private string[] Found(CandidateQuery query)
        {
            return Subject.Search(query).Items.Select(i => i.CandidateId).OrderBy(i => i).ToArray();
        }

        private static CandidateQuery Radarr()
        {
            return new CandidateQuery { IncludeTv = false, IncludeMovies = true };
        }

        private static CandidateQuery Whisparr()
        {
            return new CandidateQuery { IncludeTv = false, IncludeMovies = false, IncludeAdult = true };
        }

        [Test]
        public void radarr_searching_by_id_should_find_only_the_movie()
        {
            var query = Radarr();
            query.TmdbId = SharedTmdbId;

            Found(query).Should().Equal("radarr");
        }

        [Test]
        public void whisparr_searching_by_id_should_find_only_the_film()
        {
            var query = Whisparr();
            query.TmdbId = SharedTmdbId;

            Found(query).Should().Equal("film");
        }

        [Test]
        public void whisparr_searching_for_a_scene_by_studio_and_date_should_find_it()
        {
            var query = Whisparr();
            query.Text = "CouchCastingX 24.10.13";

            Found(query).Should().Equal("scene");
        }

        [Test]
        public void whisparr_searching_for_a_scene_by_studio_and_title_should_find_it()
        {
            var query = Whisparr();
            query.Text = "Couch Casting X Scene Title";

            Found(query).Should().Equal("scene");
        }

        [Test]
        public void a_text_search_for_movies_should_not_find_adult_content()
        {
            var query = Radarr();
            query.Text = "Scene Title";

            Found(query).Should().BeEmpty();
        }

        [Test]
        public void a_search_with_no_category_should_not_find_adult_content()
        {
            Found(new CandidateQuery { Text = "Film" }).Should().BeEmpty();
            Found(new CandidateQuery()).Should().Equal("radarr");
        }

        [Test]
        public void browsing_adult_content_should_list_only_adult_content()
        {
            Found(Whisparr()).Should().Equal("film", "scene");
        }

        [Test]
        public void browsing_movies_should_list_no_adult_content()
        {
            Found(Radarr()).Should().Equal("radarr");
        }
    }
}

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
    // A peer's albums are for Lidarr. Sonarr and Radarr never see them, whatever they ask.
    [TestFixture]
    public class MusicCandidateFixture : DbTest<CandidateService, PeerCatalogItem>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());

            var alice = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "alice", Name = "Alice", State = PeerState.Trusted, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });

            Db.Insert(new PeerCatalogItem
            {
                PeerId = alice.Id, RemoteId = "m-2-701", CandidateId = "movie", MediaKind = MediaKind.Movie, TmdbId = 10331,
                Title = "Night of the Living Dead", Year = 1968, ReleaseTitle = "Night.of.the.Living.Dead.1968.720p.WEB-DL", DateAdded = DateTime.UtcNow.AddDays(-2)
            });

            Db.Insert(new PeerCatalogItem
            {
                PeerId = alice.Id, RemoteId = "a-3-70", CandidateId = "album", MediaKind = MediaKind.Music, Artist = "Night", Title = "Living Dead", Year = 1968,
                ForeignAlbumId = "living-dead", Tracks = "[{\"Id\":\"t-3-701\",\"Extension\":\".flac\",\"Size\":10}]",
                ReleaseTitle = "Night - Living Dead (1968) [FLAC]", DateAdded = DateTime.UtcNow.AddDays(-1)
            });

            Db.Insert(new PeerCatalogItem
            {
                PeerId = alice.Id, RemoteId = "a-3-71", CandidateId = "low", MediaKind = MediaKind.Music, Artist = "Low", Title = "Things We Lost in the Fire", Year = 2001,
                ForeignAlbumId = "things-we-lost", Tracks = "[{\"Id\":\"t-3-711\",\"Extension\":\".flac\",\"Size\":10}]",
                ReleaseTitle = "Low - Things We Lost in the Fire (2001) [FLAC]", DateAdded = DateTime.UtcNow.AddDays(-3)
            });

            // An album whose tracks would not do: kept, never found.
            Db.Insert(new PeerCatalogItem
            {
                PeerId = alice.Id, RemoteId = "a-3-72", CandidateId = "broken", MediaKind = MediaKind.Music, Artist = "Low", Title = "Secret Name", Year = 1999,
                ReleaseTitle = "Low - Secret Name (1999) [FLAC]", DateAdded = DateTime.UtcNow
            });
        }

        private static CandidateQuery Lidarr(string artist = null, string album = null)
        {
            return new CandidateQuery { IncludeTv = false, IncludeMovies = false, IncludeMusic = true, Artist = artist, Album = album };
        }

        private string[] Found(CandidateQuery query)
        {
            return Subject.Search(query).Items.Select(i => i.CandidateId).OrderBy(i => i).ToArray();
        }

        [Test]
        public void browsing_with_no_category_should_list_no_albums()
        {
            Found(new CandidateQuery()).Should().Equal("movie");
        }

        [Test]
        public void browsing_movies_or_tv_should_list_no_albums()
        {
            Found(new CandidateQuery { IncludeTv = false }).Should().Equal("movie");
            Found(new CandidateQuery { IncludeMovies = false }).Should().BeEmpty();
        }

        [Test]
        public void a_text_search_should_find_no_albums()
        {
            Found(new CandidateQuery { Text = "Living Dead" }).Should().Equal("movie");
        }

        [Test]
        public void lidarr_searching_by_artist_and_album_should_find_the_album()
        {
            Found(Lidarr("Low", "Things We Lost in the Fire")).Should().Equal("low");
        }

        [Test]
        public void lidarr_searching_by_artist_should_find_each_of_its_albums_that_can_be_fetched()
        {
            Found(Lidarr("Low")).Should().Equal("low");
        }

        [Test]
        public void lidarr_searching_for_another_album_should_find_nothing()
        {
            Found(Lidarr("Low", "Trust")).Should().BeEmpty();
        }

        [Test]
        public void lidarr_should_never_find_a_movie()
        {
            Found(Lidarr("Night", "Living Dead")).Should().Equal("album");
        }

        [Test]
        public void lidarr_browsing_should_list_only_albums_that_can_be_fetched()
        {
            Found(Lidarr()).Should().Equal("album", "low");
        }

        [Test]
        public void a_plain_search_in_the_audio_categories_should_find_albums_by_their_words()
        {
            Found(new CandidateQuery { IncludeTv = false, IncludeMovies = false, IncludeMusic = true, Text = "low fire" }).Should().Equal("low");
        }
    }
}

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
    }
}

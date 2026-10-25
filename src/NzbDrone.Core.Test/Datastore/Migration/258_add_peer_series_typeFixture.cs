using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_peer_series_typeFixture : MigrationTest<add_peer_series_type>
    {
        private class PeerItem258
        {
            public string RemoteId { get; set; }
            public string SeriesType { get; set; }
        }

        [Test]
        public void existing_offers_should_have_an_unknown_series_type_until_their_next_sync()
        {
            var db = WithMigrationTestDb(c => c.Insert.IntoTable("PeerCatalogItems").Row(new
            {
                PeerId = 1,
                RemoteId = "e-1-1",
                CandidateId = "candidate",
                MediaKind = 0,
                TvdbId = 75669,
                TmdbId = 0,
                Year = 1986,
                SeasonNumber = 1,
                Resolution = 720,
                Size = 100L,
                LocalStatus = 0
            }));

            db.Query<PeerItem258>("SELECT \"RemoteId\", \"SeriesType\" FROM \"PeerCatalogItems\"")
              .Single().Should().Match<PeerItem258>(i => i.RemoteId == "e-1-1" && i.SeriesType == null);
        }
    }
}

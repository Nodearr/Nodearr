using System;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_rss_arrival_timesFixture : MigrationTest<add_rss_arrival_times>
    {
        [Test]
        public void upgrade_should_baseline_existing_catalogs_without_announcing_their_rows()
        {
            var synced = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
            using var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Peers").Row(new
                {
                    Id = 1, NodeId = "alice", State = 1, Added = synced, StateChanged = synced, CatalogSyncedAt = synced
                });
                m.Insert.IntoTable("Peers").Row(new
                {
                    Id = 2, NodeId = "bob", State = 1, Added = synced, StateChanged = synced
                });
                m.Insert.IntoTable("PeerCatalogItems").Row(new
                {
                    PeerId = 1, RemoteId = "episode", MediaKind = 0, TvdbId = 75669, TmdbId = 0, Year = 1989,
                    SeasonNumber = 1, Resolution = 720, Size = 100L, LocalStatus = 1
                });
            });

            db.QuerySingle<DateTime>("SELECT [CatalogFirstSyncedAt] FROM [Peers] WHERE [Id] = 1").Should().Be(synced);
            db.QuerySingle<DateTime?>("SELECT [CatalogFirstSyncedAt] FROM [Peers] WHERE [Id] = 2").Should().BeNull();
            db.QuerySingle<DateTime?>("SELECT [FirstSeen] FROM [PeerCatalogItems]").Should().BeNull();
            db.QuerySingle<int>("SELECT COUNT(*) FROM [PeerCatalogItems] i JOIN [Peers] p ON p.[Id] = i.[PeerId] WHERE i.[FirstSeen] > p.[CatalogFirstSyncedAt]").Should().Be(0);
        }
    }
}

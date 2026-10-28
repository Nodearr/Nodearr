using System;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class refresh_shared_catalog_detailsFixture : MigrationTest<refresh_shared_catalog_details>
    {
        [Test]
        public void should_invalidate_versions_without_discarding_cached_files_or_arrival_baselines()
        {
            var baseline = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
            using var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Peers").Row(new
                {
                    Id = 1, NodeId = "alice", Name = "Alice", State = 1, Added = baseline, StateChanged = baseline,
                    ShareAll = false, CatalogVersion = "unchanged", CatalogFirstSyncedAt = baseline, FetchNewFilesAutomatically = false
                });
                m.Insert.IntoTable("PeerCatalogItems").Row(new
                {
                    PeerId = 1, RemoteId = "e-1", MediaKind = 0, TvdbId = 5, TmdbId = 0, Title = "Pingu", Year = 1986,
                    SeasonNumber = 1, EpisodeNumbers = "1", Resolution = 720, Size = 100L, LocalStatus = 0,
                    Fingerprint = "same-fingerprint", FirstSeen = baseline
                });
            });

            db.QuerySingle<string>("SELECT [CatalogVersion] FROM [Peers]").Should().BeNull();
            db.QuerySingle<DateTime>("SELECT [CatalogFirstSyncedAt] FROM [Peers]").Should().Be(baseline);
            db.QuerySingle<int>("SELECT [ShareAll] FROM [Peers]").Should().Be(0);
            db.QuerySingle<int>("SELECT [FetchNewFilesAutomatically] FROM [Peers]").Should().Be(0);
            db.QuerySingle<string>("SELECT [Fingerprint] FROM [PeerCatalogItems]").Should().Be("same-fingerprint");
            db.QuerySingle<DateTime>("SELECT [FirstSeen] FROM [PeerCatalogItems]").Should().Be(baseline);
            db.QuerySingle<string>("SELECT [Title] FROM [PeerCatalogItems]").Should().Be("Pingu");
        }
    }
}

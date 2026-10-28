using System;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_peer_automatic_fetchFixture : MigrationTest<add_peer_automatic_fetch>
    {
        [Test]
        public void existing_peers_and_new_peers_default_to_enabled()
        {
            var now = DateTime.UtcNow;
            using var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Peers").Row(new { Id = 1, NodeId = "alice", Name = "Alice", State = 1, Added = now, StateChanged = now, ShareAll = true });
                m.Insert.IntoTable("Peers").Row(new { Id = 2, NodeId = "bob", Name = "Bob", State = 3, Added = now, StateChanged = now, ShareAll = false });
            });

            db.QuerySingle<int>("SELECT [FetchNewFilesAutomatically] FROM [Peers] WHERE [Id] = 1").Should().Be(1);
            db.QuerySingle<int>("SELECT [FetchNewFilesAutomatically] FROM [Peers] WHERE [Id] = 2").Should().Be(1);
            db.QuerySingle<int>("SELECT [ShareAll] FROM [Peers] WHERE [Id] = 1").Should().Be(1);
            db.QuerySingle<string>("SELECT [Name] FROM [Peers] WHERE [Id] = 2").Should().Be("Bob");
            new Peer().FetchNewFilesAutomatically.Should().BeTrue();
        }
    }
}

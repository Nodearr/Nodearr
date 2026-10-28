using System;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_transfer_waiting_positionFixture : MigrationTest<add_transfer_waiting_position>
    {
        [Test]
        public void a_transfer_from_before_is_not_waiting_for_anyone()
        {
            var now = DateTime.UtcNow;
            using var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Transfers").Row(new
                {
                    Id = 1, DownloadId = "SABnzbd_nzo_1", CandidateId = "c-1", PeerId = 1, PeerNodeId = "alice", RemoteId = "e-1",
                    MediaKind = 0, Title = "Pingu.S01E01", Size = 1000, BytesDone = 500, State = 1, Failure = 0, Attempts = 1, Added = now
                });
            });

            db.QuerySingle<int?>("SELECT [WaitingPosition] FROM [Transfers] WHERE [Id] = 1").Should().BeNull();
            db.QuerySingle<long>("SELECT [BytesDone] FROM [Transfers] WHERE [Id] = 1").Should().Be(500);
        }
    }
}

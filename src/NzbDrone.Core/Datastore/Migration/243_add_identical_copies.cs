using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(243)]
    public class add_identical_copies : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Files from different nodes with the same hash are the same bytes.
            Create.Index().OnTable("PeerCatalogItems").OnColumn("Blake3").Ascending();

            // A transfer knows the hash of the file it is after, so it can carry on from another node
            // that has the same file, and which nodes it has already tried.
            Alter.Table("Transfers")
                .AddColumn("Blake3").AsString().Nullable()
                .AddColumn("TriedPeerIds").AsString().Nullable();
        }
    }
}

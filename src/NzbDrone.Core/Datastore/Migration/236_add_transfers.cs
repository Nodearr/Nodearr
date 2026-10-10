using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(236)]
    public class add_transfers : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("Transfers")
                .WithColumn("DownloadId").AsString().Unique()
                .WithColumn("CandidateId").AsString()
                .WithColumn("PeerId").AsInt32()
                .WithColumn("PeerNodeId").AsString()
                .WithColumn("RemoteId").AsString()
                .WithColumn("MediaKind").AsInt32()
                .WithColumn("Title").AsString()
                .WithColumn("Extension").AsString().Nullable()
                .WithColumn("Category").AsString().Nullable()
                .WithColumn("Size").AsInt64()
                .WithColumn("BytesDone").AsInt64()
                .WithColumn("State").AsInt32()
                .WithColumn("Failure").AsInt32()
                .WithColumn("FailureMessage").AsString().Nullable()
                .WithColumn("Hash").AsString().Nullable()
                .WithColumn("OutputPath").AsString().Nullable()
                .WithColumn("Attempts").AsInt32()
                .WithColumn("NextAttempt").AsDateTimeOffset().Nullable()
                .WithColumn("Added").AsDateTimeOffset()
                .WithColumn("Started").AsDateTimeOffset().Nullable()
                .WithColumn("Ended").AsDateTimeOffset().Nullable();

            // The file a transfer writes needs the right extension for the Arr app to import it.
            Alter.Table("PeerCatalogItems").AddColumn("Extension").AsString().Nullable();

            // Fetch every peer's catalog again, with the extension filled in.
            Execute.Sql("DELETE FROM \"PeerCatalogItems\"");
            Execute.Sql("UPDATE \"Peers\" SET \"CatalogVersion\" = NULL");
        }
    }
}

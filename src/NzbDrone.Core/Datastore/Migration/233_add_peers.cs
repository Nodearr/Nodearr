using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(233)]
    public class add_peers : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("Peers")
                .WithColumn("NodeId").AsString().Unique()
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("State").AsInt32()
                .WithColumn("StateReason").AsString().Nullable()
                .WithColumn("RelayUrl").AsString().Nullable()
                .WithColumn("IpAddresses").AsString().Nullable()
                .WithColumn("Added").AsDateTimeOffset()
                .WithColumn("StateChanged").AsDateTimeOffset()
                .WithColumn("LastSeen").AsDateTimeOffset().Nullable()
                .WithColumn("LastError").AsString().Nullable();

            Create.TableForModel("PeerInvites")
                .WithColumn("SecretHash").AsString().Unique()
                .WithColumn("Label").AsString().Nullable()
                .WithColumn("Created").AsDateTimeOffset()
                .WithColumn("Expires").AsDateTimeOffset()
                .WithColumn("RedeemedAt").AsDateTimeOffset().Nullable()
                .WithColumn("RedeemedByNodeId").AsString().Nullable();
        }
    }
}

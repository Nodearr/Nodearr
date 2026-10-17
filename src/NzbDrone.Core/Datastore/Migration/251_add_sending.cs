using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(251)]
    public class add_sending : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // What this node sent friends, and the list of its own each friend's sends are shared on.
            Create.TableForModel("SentTitles")
                .WithColumn("SendId").AsString().Unique()
                .WithColumn("PeerId").AsInt32().Nullable()
                .WithColumn("InviteId").AsInt32().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("Summary").AsString().Nullable()
                .WithColumn("MediaKind").AsInt32()
                .WithColumn("Items").AsString()
                .WithColumn("Created").AsDateTimeOffset()
                .WithColumn("DeliveredAt").AsDateTimeOffset().Nullable()
                .WithColumn("LastError").AsString().Nullable();

            Alter.Table("ShareLists").AddColumn("SentToPeerId").AsInt32().Nullable();

            // What friends sent this node.
            Create.TableForModel("ReceivedTitles")
                .WithColumn("PeerId").AsInt32()
                .WithColumn("SendId").AsString()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("Summary").AsString().Nullable()
                .WithColumn("MediaKind").AsInt32()
                .WithColumn("IsWhole").AsBoolean()
                .WithColumn("OfferIds").AsString().Nullable()
                .WithColumn("Received").AsDateTimeOffset()
                .WithColumn("Handled").AsDateTimeOffset().Nullable();

            Create.Index().OnTable("ReceivedTitles").OnColumn("PeerId").Ascending().OnColumn("SendId").Ascending().WithOptions().Unique();
        }
    }
}

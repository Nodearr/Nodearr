using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(234)]
    public class add_sharing_and_peer_catalog : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // A peer sees nothing until something is shared with it.
            Alter.Table("Peers")
                .AddColumn("ShareAll").AsBoolean().WithDefaultValue(false)
                .AddColumn("CatalogVersion").AsString().Nullable()
                .AddColumn("CatalogSyncedAt").AsDateTimeOffset().Nullable()
                .AddColumn("CatalogError").AsString().Nullable();

            Alter.Table("PeerInvites")
                .AddColumn("ShareAll").AsBoolean().WithDefaultValue(false);

            Create.TableForModel("ShareLists")
                .WithColumn("Name").AsString()
                .WithColumn("IsHidden").AsBoolean();

            Create.TableForModel("ShareListItems")
                .WithColumn("ShareListId").AsInt32()
                .WithColumn("MediaKind").AsInt32()
                .WithColumn("ApplicationId").AsInt32()
                .WithColumn("ArrId").AsInt32();

            Create.TableForModel("ShareListPeers")
                .WithColumn("ShareListId").AsInt32()
                .WithColumn("PeerId").AsInt32();

            Create.Index().OnTable("ShareListItems").OnColumn("ShareListId").Ascending();
            Create.Index().OnTable("ShareListPeers").OnColumn("ShareListId").Ascending();

            Create.TableForModel("PeerCatalogItems")
                .WithColumn("PeerId").AsInt32()
                .WithColumn("RemoteId").AsString()
                .WithColumn("MediaKind").AsInt32()
                .WithColumn("TvdbId").AsInt32()
                .WithColumn("TmdbId").AsInt32()
                .WithColumn("ImdbId").AsString().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("Year").AsInt32()
                .WithColumn("SeasonNumber").AsInt32()
                .WithColumn("EpisodeNumbers").AsString().Nullable()
                .WithColumn("EpisodeTitles").AsString().Nullable()
                .WithColumn("ReleaseTitle").AsString().Nullable()
                .WithColumn("Quality").AsString().Nullable()
                .WithColumn("QualitySource").AsString().Nullable()
                .WithColumn("Resolution").AsInt32()
                .WithColumn("Size").AsInt64()
                .WithColumn("Languages").AsString().Nullable()
                .WithColumn("CustomFormats").AsString().Nullable()
                .WithColumn("ReleaseGroup").AsString().Nullable()
                .WithColumn("VideoCodec").AsString().Nullable()
                .WithColumn("AudioCodec").AsString().Nullable()
                .WithColumn("DateAdded").AsDateTimeOffset().Nullable()
                .WithColumn("LocalStatus").AsInt32();

            Create.Index().OnTable("PeerCatalogItems").OnColumn("PeerId").Ascending();
        }
    }
}

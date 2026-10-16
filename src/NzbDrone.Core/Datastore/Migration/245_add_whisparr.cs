using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(245)]
    public class add_whisparr : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // What Whisparr knows about a film or a scene beyond what Radarr knows about a movie.
            Alter.Table("CatalogMovies")
                .AddColumn("ItemType").AsString().Nullable()
                .AddColumn("Studio").AsString().Nullable()
                .AddColumn("ReleaseDate").AsString().Nullable()
                .AddColumn("Code").AsString().Nullable()
                .AddColumn("ForeignId").AsString().Nullable()
                .AddColumn("StashId").AsString().Nullable()
                .AddColumn("TpdbId").AsString().Nullable();

            // The same, as a peer offers it. A scene is searched for by its release date.
            Alter.Table("PeerCatalogItems")
                .AddColumn("IsAdult").AsBoolean().NotNullable().WithDefaultValue(false)
                .AddColumn("ItemType").AsString().Nullable()
                .AddColumn("Studio").AsString().Nullable()
                .AddColumn("ReleaseDate").AsString().Nullable()
                .AddColumn("Code").AsString().Nullable()
                .AddColumn("ForeignId").AsString().Nullable()
                .AddColumn("StashId").AsString().Nullable()
                .AddColumn("TpdbId").AsString().Nullable();

            Create.Index().OnTable("PeerCatalogItems").OnColumn("ReleaseDate").Ascending();
        }
    }
}

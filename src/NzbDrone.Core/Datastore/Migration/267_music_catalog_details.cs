using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(267)]
    public class music_catalog_details : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("CatalogArtists")
                .AddColumn("Overview").AsString().Nullable()
                .AddColumn("Genres").AsString().Nullable();
            Alter.Table("CatalogAlbums")
                .AddColumn("Overview").AsString().Nullable()
                .AddColumn("Genres").AsString().Nullable()
                .AddColumn("Tracks").AsString().Nullable();
            Alter.Table("PeerCatalogItems")
                .AddColumn("ArtistOverview").AsString().Nullable()
                .AddColumn("ArtistGenres").AsString().Nullable();
            Alter.Table("CatalogTrackFiles").AddColumn("ForeignRecordingId").AsString().Nullable();

            // Older receivers discarded music details while retaining their fingerprints.
            Execute.Sql("UPDATE \"Peers\" SET \"CatalogVersion\" = NULL");
        }
    }
}

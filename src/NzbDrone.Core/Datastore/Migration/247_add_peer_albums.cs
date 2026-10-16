using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(247)]
    public class add_peer_albums : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // A peer's album from Lidarr: its artist, the MusicBrainz IDs, and its tracks as JSON.
            Alter.Table("PeerCatalogItems")
                .AddColumn("Artist").AsString().Nullable()
                .AddColumn("ForeignArtistId").AsString().Nullable()
                .AddColumn("ForeignAlbumId").AsString().Nullable()
                .AddColumn("AlbumType").AsString().Nullable()
                .AddColumn("Tracks").AsString().Nullable();

            Create.Index().OnTable("PeerCatalogItems").OnColumn("ForeignAlbumId").Ascending();
        }
    }
}

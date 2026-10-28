using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(261)]
    public class add_rss_arrival_times : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("PeerCatalogItems").AddColumn("FirstSeen").AsDateTimeOffset().Nullable();
            Alter.Table("Peers").AddColumn("CatalogFirstSyncedAt").AsDateTimeOffset().Nullable();
            Execute.Sql("UPDATE \"Peers\" SET \"CatalogFirstSyncedAt\" = \"CatalogSyncedAt\"");
        }
    }
}

using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(265)]
    public class refresh_shared_catalog_details : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // An older receiver could keep the sender's current fingerprints while discarding
            // fields added in migration 260. Request a whole offer once, even if it is unchanged.
            // Keep cached files and arrival dates so this refresh does not announce old files as new.
            Execute.Sql("UPDATE \"Peers\" SET \"CatalogVersion\" = NULL");
        }
    }
}

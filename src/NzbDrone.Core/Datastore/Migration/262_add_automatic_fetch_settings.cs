using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(262)]
    public class add_automatic_fetch_settings : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Registration sync reads the Arr app's existing RSS switch before taking it over.
            // No network request belongs in a database migration.
            Execute.Sql(@"UPDATE [Applications]
                SET [Settings] = json_set(COALESCE([Settings], '{}'),
                    '$.fetchNewFilesAutomatically', NULL, '$.includeAutomaticUpgrades', json('false'))
                WHERE [Implementation] != 'Prowlarr'");
        }
    }
}

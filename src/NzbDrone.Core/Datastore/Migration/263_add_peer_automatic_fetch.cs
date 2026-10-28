using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(263)]
    public class add_peer_automatic_fetch : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Peers").AddColumn("FetchNewFilesAutomatically").AsBoolean().WithDefaultValue(true);
        }
    }
}

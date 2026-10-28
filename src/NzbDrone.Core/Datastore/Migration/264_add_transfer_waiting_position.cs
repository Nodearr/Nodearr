using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(264)]
    public class add_transfer_waiting_position : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Transfers").AddColumn("WaitingPosition").AsInt32().Nullable();
        }
    }
}

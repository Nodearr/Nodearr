using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(266)]
    public class received_already_held_seen : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ReceivedTitles").AddColumn("AlreadyHeldSeenAt").AsDateTime().Nullable();
        }
    }
}

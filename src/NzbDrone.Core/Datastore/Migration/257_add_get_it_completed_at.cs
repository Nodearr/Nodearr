using System;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(257)]
    public class add_get_it_completed_at : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ReceivedTitles").AddColumn("GetItCompletedAt").AsDateTimeOffset().Nullable();

            // Older successes have no completion time. Give them a full day from the upgrade.
            Update.Table("ReceivedTitles").Set(new { GetItCompletedAt = DateTime.UtcNow }).Where(new { GetItState = 2 });
        }
    }
}

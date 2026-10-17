using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(253)]
    public class add_get_it_adult_ids : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // What Whisparr knows a film or a scene a friend sent by, so it can be added (Get it).
            Alter.Table("ReceivedTitles")
                .AddColumn("ForeignId").AsString().Nullable()
                .AddColumn("StashId").AsString().Nullable()
                .AddColumn("TpdbId").AsString().Nullable();
        }
    }
}

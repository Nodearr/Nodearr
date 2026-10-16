using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(249)]
    public class add_track_titles : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Which track of the album each file is, as Lidarr has it, so a friend can name it.
            Alter.Table("CatalogTrackFiles")
                .AddColumn("TrackTitle").AsString().Nullable()
                .AddColumn("TrackNumber").AsInt32().WithDefaultValue(0)
                .AddColumn("DiscNumber").AsInt32().WithDefaultValue(0);
        }
    }
}

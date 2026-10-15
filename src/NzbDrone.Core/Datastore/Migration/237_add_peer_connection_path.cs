using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(237)]
    public class add_peer_connection_path : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // What the last connection check found: direct, through a relay, or not known.
            Alter.Table("Peers")
                .AddColumn("ConnectionPath").AsInt32().WithDefaultValue(0)
                .AddColumn("ConnectionAddress").AsString().Nullable()
                .AddColumn("ConnectionChecked").AsDateTimeOffset().Nullable();
        }
    }
}

using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(258)]
    public class add_peer_series_type : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("PeerCatalogItems").AddColumn("SeriesType").AsString().Nullable();
        }
    }
}

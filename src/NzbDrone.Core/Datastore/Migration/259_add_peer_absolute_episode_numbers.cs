using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(259)]
    public class add_peer_absolute_episode_numbers : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("PeerCatalogItems").AddColumn("AbsoluteEpisodeNumbers").AsString().Nullable();
        }
    }
}

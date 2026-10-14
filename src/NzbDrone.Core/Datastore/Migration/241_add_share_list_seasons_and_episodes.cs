using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(241)]
    public class add_share_list_seasons_and_episodes : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // A share list can hold one season of a series, or one episode, as well as the whole
            // series, the way Sonarr monitors them. Rows already there are whole series or movies.
            Alter.Table("ShareListItems")
                .AddColumn("SeasonNumber").AsInt32().Nullable()
                .AddColumn("EpisodeArrId").AsInt32().Nullable();
        }
    }
}

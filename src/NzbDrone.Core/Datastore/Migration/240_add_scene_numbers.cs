using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(240)]
    public class add_scene_numbers : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Where the scene numbers an episode differently from TVDB, as Sonarr has it. Sonarr
            // searches by these numbers, and reads them in release titles, for such a series.
            Alter.Table("CatalogEpisodes")
                .AddColumn("SceneSeasonNumber").AsInt32().Nullable()
                .AddColumn("SceneEpisodeNumber").AsInt32().Nullable();

            // The same for what a peer offers: one season, and one number for each episode in the file.
            Alter.Table("PeerCatalogItems")
                .AddColumn("SceneSeasonNumber").AsInt32().Nullable()
                .AddColumn("SceneEpisodeNumbers").AsString().Nullable();
        }
    }
}

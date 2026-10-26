using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(260)]
    public class add_catalog_details : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("CatalogSeries")
                .AddColumn("Overview").AsString().Nullable()
                .AddColumn("Genres").AsString().Nullable()
                .AddColumn("Network").AsString().Nullable()
                .AddColumn("Runtime").AsInt32().WithDefaultValue(0);

            Alter.Table("CatalogEpisodes")
                .AddColumn("Overview").AsString().Nullable();

            Alter.Table("CatalogMovies")
                .AddColumn("Overview").AsString().Nullable()
                .AddColumn("Genres").AsString().Nullable()
                .AddColumn("Runtime").AsInt32().WithDefaultValue(0);

            Alter.Table("PeerCatalogItems")
                .AddColumn("Overview").AsString().Nullable()
                .AddColumn("Genres").AsString().Nullable()
                .AddColumn("Network").AsString().Nullable()
                .AddColumn("Runtime").AsInt32().WithDefaultValue(0)
                .AddColumn("EpisodeDetails").AsString().Nullable();
        }
    }
}

using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(235)]
    public class add_peer_candidates : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("PeerCatalogItems")
                .AddColumn("CandidateId").AsString().Nullable()
                .AddColumn("SceneName").AsString().Nullable()
                .AddColumn("Edition").AsString().Nullable()
                .AddColumn("VideoDynamicRange").AsString().Nullable()
                .AddColumn("AudioChannels").AsDouble().WithDefaultValue(0);

            Create.Index().OnTable("PeerCatalogItems").OnColumn("CandidateId").Ascending();
            Create.Index().OnTable("PeerCatalogItems").OnColumn("TvdbId").Ascending();
            Create.Index().OnTable("PeerCatalogItems").OnColumn("TmdbId").Ascending();
            Create.Index().OnTable("PeerCatalogItems").OnColumn("ImdbId").Ascending();

            // These rows are copies of what peers offer. Dropping them makes every peer's catalog
            // be fetched again, with the new fields filled in.
            Execute.Sql("DELETE FROM \"PeerCatalogItems\"");
            Execute.Sql("UPDATE \"Peers\" SET \"CatalogVersion\" = NULL");
        }
    }
}

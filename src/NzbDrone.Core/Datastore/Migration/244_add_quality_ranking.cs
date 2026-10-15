using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(244)]
    public class add_quality_ranking : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The order a quality profile puts its qualities in, so that what is better than a local
            // file is decided as the Arr app decides it. Filled in when the profiles are next read.
            Alter.Table("CatalogQualityProfiles")
                .AddColumn("QualityRanking").AsString().Nullable();
        }
    }
}

using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(238)]
    public class add_air_dates : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The day an episode aired, as yyyy-MM-dd. Sonarr asks for daily shows by it.
            Alter.Table("CatalogEpisodes").AddColumn("AirDate").AsString().Nullable();

            // The same for what a peer offers, one day for each episode in the file.
            Alter.Table("PeerCatalogItems").AddColumn("AirDates").AsString().Nullable();

            // Set on a transfer that was asked for by day, which decides how it is titled.
            Alter.Table("Transfers").AddColumn("AirDate").AsString().Nullable();
        }
    }
}

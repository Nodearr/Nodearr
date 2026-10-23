using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(255)]
    public class remove_import_lists_and_trakt_that_sign_in_through_servarr : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // These signed in through auth.servarr.com apps named for Nodearr, which Servarr does not
            // have, or fetched from services.nodearr.com, which serves nothing, so they are gone. One
            // could only have been added through Sonarr's API, also gone.
            foreach (var implementation in new[] { "AniListImport", "MyAnimeListImport", "SimklUserImport", "TraktListImport", "TraktPopularImport", "TraktUserImport" })
            {
                Delete.FromTable("ImportLists").Row(new { Implementation = implementation });
            }

            Delete.FromTable("Notifications").Row(new { Implementation = "Trakt" });
        }
    }
}

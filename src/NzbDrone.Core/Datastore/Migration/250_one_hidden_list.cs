using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(250)]
    public class one_hidden_list : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Two requests at once could each make a Hidden list. What was ticked on any of them is
            // moved to the first, the others go, and the database keeps it to one from now on.
            Execute.Sql(@"UPDATE ""ShareListItems"" SET ""ShareListId"" = (SELECT MIN(""Id"") FROM ""ShareLists"" WHERE ""IsHidden"" = true)
                          WHERE ""ShareListId"" IN (SELECT ""Id"" FROM ""ShareLists"" WHERE ""IsHidden"" = true)");
            Execute.Sql(@"DELETE FROM ""ShareListPeers"" WHERE ""ShareListId"" IN (SELECT ""Id"" FROM ""ShareLists"" WHERE ""IsHidden"" = true)");
            Execute.Sql(@"DELETE FROM ""ShareLists"" WHERE ""IsHidden"" = true AND ""Id"" <> (SELECT MIN(""Id"") FROM ""ShareLists"" WHERE ""IsHidden"" = true)");
            Execute.Sql(@"CREATE UNIQUE INDEX ""IX_ShareLists_OneHidden"" ON ""ShareLists"" (""IsHidden"") WHERE ""IsHidden"" = true");
        }
    }
}

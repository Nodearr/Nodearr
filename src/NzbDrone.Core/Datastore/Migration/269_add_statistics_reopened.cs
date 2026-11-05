using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // A failed upload that a friend asks for again was taken back by lowering its hour's count of
    // transfers. Prometheus reads a total that goes down as a restart, so the take-back is now
    // kept beside the count instead (TransferStatistic.Reopened). Rows from before this kept no
    // record of it and start from zero.
    [Migration(269)]
    public class add_statistics_reopened : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("TransferStatistics")
                .AddColumn("Reopened").AsInt32().WithDefaultValue(0);
        }
    }
}

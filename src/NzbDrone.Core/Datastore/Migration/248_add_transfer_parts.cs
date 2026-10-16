using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(248)]
    public class add_transfer_parts : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // An album arrives as its tracks, one after another: the parts as JSON, and which
            // part is under way.
            Alter.Table("Transfers")
                .AddColumn("Parts").AsString().Nullable()
                .AddColumn("PartIndex").AsInt32().WithDefaultValue(0);
        }
    }
}

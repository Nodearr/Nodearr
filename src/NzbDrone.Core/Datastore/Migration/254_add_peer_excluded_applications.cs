using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(254)]
    public class add_peer_excluded_applications : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The Arr apps a friend offered everything is not offered from. A friend there already
            // keeps what they had: every app.
            Alter.Table("Peers").AddColumn("ExcludedApplicationIds").AsString().WithDefaultValue("[]");
        }
    }
}

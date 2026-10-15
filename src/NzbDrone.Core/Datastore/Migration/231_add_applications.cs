using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(231)]
    public class add_applications : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("Applications")
                  .WithColumn("Name").AsString().Unique()
                  .WithColumn("Implementation").AsString()
                  .WithColumn("Settings").AsString().Nullable()
                  .WithColumn("ConfigContract").AsString().Nullable()
                  .WithColumn("SyncLevel").AsInt32()
                  .WithColumn("Tags").AsString().Nullable();

            Create.TableForModel("ApplicationIndexerMapping")
                  .WithColumn("IndexerId").AsInt32()
                  .WithColumn("AppId").AsInt32()
                  .WithColumn("RemoteIndexerId").AsInt32()
                  .WithColumn("RemoteIndexerName").AsString().Nullable();

            Create.TableForModel("ApplicationStatus")
                  .WithColumn("ProviderId").AsInt32().NotNullable().Unique()
                  .WithColumn("InitialFailure").AsDateTime().Nullable()
                  .WithColumn("MostRecentFailure").AsDateTime().Nullable()
                  .WithColumn("EscalationLevel").AsInt32().NotNullable()
                  .WithColumn("DisabledTill").AsDateTime().Nullable();
        }
    }
}

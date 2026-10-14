using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(239)]
    public class add_file_hashes : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The hash of each shared file, worked out in the background, and the version of the
            // file it belongs to. Keyed by where the file is on this machine.
            Create.TableForModel("FileHashes")
                .WithColumn("Path").AsString().Unique()
                .WithColumn("Size").AsInt64()
                .WithColumn("ModifiedNs").AsInt64()
                .WithColumn("Blake3").AsString()
                .WithColumn("Hashed").AsDateTimeOffset();
        }
    }
}

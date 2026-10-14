using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(242)]
    public class add_catalog_changes : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Each file a peer offers carries a fingerprint of what is said about it, and its
            // BLAKE3 hash where the peer has worked it out. A catalog's version is made from the
            // fingerprints, so this node can check what it holds against what the peer says.
            Alter.Table("PeerCatalogItems")
                .AddColumn("Fingerprint").AsString().Nullable()
                .AddColumn("Blake3").AsString().Nullable();

            // What this node last sent each peer: the version, and each file's fingerprint. A peer
            // that holds that version is sent only what changed since.
            Create.TableForModel("OfferRecords")
                .WithColumn("PeerId").AsInt32().Unique()
                .WithColumn("Version").AsString()
                .WithColumn("Fingerprints").AsString()
                .WithColumn("Sent").AsDateTimeOffset();

            // Which offered file a kept hash is for, so an offer can carry it without looking for
            // the file on disk.
            Alter.Table("FileHashes").AddColumn("OfferFileId").AsString().Nullable();

            // Versions are worked out differently now. Fetch every peer's catalog in full once.
            Execute.Sql("UPDATE \"Peers\" SET \"CatalogVersion\" = NULL");
        }
    }
}

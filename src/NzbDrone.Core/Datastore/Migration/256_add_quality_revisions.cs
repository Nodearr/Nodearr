using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(256)]
    public class add_quality_revisions : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // A file's quality revision, as its Arr app recorded it, so that a PROPER or a REPACK is
            // offered as one. Every file known so far is taken as a first release until its Arr app
            // is next read, and every peer's until it next sends its catalog.
            foreach (var table in new[] { "CatalogEpisodeFiles", "CatalogMovieFiles", "CatalogTrackFiles", "PeerCatalogItems" })
            {
                Alter.Table(table)
                    .AddColumn("RevisionVersion").AsInt32().WithDefaultValue(1)
                    .AddColumn("RevisionReal").AsInt32().WithDefaultValue(0)
                    .AddColumn("RevisionIsRepack").AsBoolean().WithDefaultValue(false);
            }
        }
    }
}

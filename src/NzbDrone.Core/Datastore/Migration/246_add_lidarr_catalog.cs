using FluentMigrator;
using FluentMigrator.Builders.Create.Table;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(246)]
    public class add_lidarr_catalog : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Lidarr's library: artists, their albums, and the albums' track files.
            CatalogTable("CatalogArtists")
                .WithColumn("ForeignArtistId").AsString().Nullable()
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("SortName").AsString().Nullable()
                .WithColumn("Path").AsString().Nullable()
                .WithColumn("QualityProfileId").AsInt32()
                .WithColumn("Monitored").AsBoolean();

            CatalogTable("CatalogAlbums")
                .WithColumn("ArrArtistId").AsInt32()
                .WithColumn("ForeignAlbumId").AsString().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("AlbumType").AsString().Nullable()
                .WithColumn("ReleaseDate").AsString().Nullable()
                .WithColumn("Year").AsInt32()
                .WithColumn("Monitored").AsBoolean()
                .WithColumn("TrackCount").AsInt32()
                .WithColumn("TrackFileCount").AsInt32();

            // The same columns as the other file tables (232), and which album and artist.
            CatalogTable("CatalogTrackFiles")
                .WithColumn("Path").AsString().Nullable()
                .WithColumn("RelativePath").AsString().Nullable()
                .WithColumn("Size").AsInt64()
                .WithColumn("DateAdded").AsDateTimeOffset().Nullable()
                .WithColumn("SceneName").AsString().Nullable()
                .WithColumn("ReleaseGroup").AsString().Nullable()
                .WithColumn("Quality").AsString().Nullable()
                .WithColumn("QualitySource").AsString().Nullable()
                .WithColumn("Resolution").AsInt32()
                .WithColumn("Languages").AsString().Nullable()
                .WithColumn("CustomFormats").AsString().Nullable()
                .WithColumn("CustomFormatScore").AsInt32()
                .WithColumn("VideoCodec").AsString().Nullable()
                .WithColumn("VideoDynamicRange").AsString().Nullable()
                .WithColumn("AudioCodec").AsString().Nullable()
                .WithColumn("AudioChannels").AsDouble()
                .WithColumn("RunTime").AsString().Nullable()
                .WithColumn("ArrArtistId").AsInt32()
                .WithColumn("ArrAlbumId").AsInt32();

            foreach (var table in new[] { "CatalogArtists", "CatalogAlbums", "CatalogTrackFiles" })
            {
                Create.Index().OnTable(table).OnColumn("ApplicationId").Ascending().OnColumn("ArrId").Ascending().WithOptions().Unique();
            }

            Create.Index().OnTable("CatalogTrackFiles").OnColumn("ApplicationId").Ascending().OnColumn("ArrAlbumId").Ascending();
        }

        private ICreateTableColumnOptionOrWithColumnSyntax CatalogTable(string name)
        {
            return Create.TableForModel(name)
                .WithColumn("ApplicationId").AsInt32()
                .WithColumn("ArrId").AsInt32()
                .WithColumn("MissingSince").AsDateTimeOffset().Nullable();
        }
    }
}

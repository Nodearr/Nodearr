using FluentMigrator;
using FluentMigrator.Builders.Create.Table;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(232)]
    public class add_catalog : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Applications now register one Nodearr indexer per Arr app instead of mapping many.
            Delete.Table("ApplicationIndexerMapping");

            CatalogTable("CatalogQualityProfiles")
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("UpgradeAllowed").AsBoolean()
                .WithColumn("CutoffQualityId").AsInt32()
                .WithColumn("AllowedQualities").AsString().Nullable();

            CatalogTable("CatalogSeries")
                .WithColumn("TvdbId").AsInt32()
                .WithColumn("TvMazeId").AsInt32()
                .WithColumn("TmdbId").AsInt32()
                .WithColumn("ImdbId").AsString().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("SortTitle").AsString().Nullable()
                .WithColumn("Year").AsInt32()
                .WithColumn("SeriesType").AsString().Nullable()
                .WithColumn("Path").AsString().Nullable()
                .WithColumn("QualityProfileId").AsInt32()
                .WithColumn("Monitored").AsBoolean();

            CatalogTable("CatalogEpisodes")
                .WithColumn("ArrSeriesId").AsInt32()
                .WithColumn("SeasonNumber").AsInt32()
                .WithColumn("EpisodeNumber").AsInt32()
                .WithColumn("AbsoluteEpisodeNumber").AsInt32().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("AirDateUtc").AsDateTimeOffset().Nullable()
                .WithColumn("Monitored").AsBoolean()
                .WithColumn("ArrEpisodeFileId").AsInt32();

            CatalogTable("CatalogMovies")
                .WithColumn("TmdbId").AsInt32()
                .WithColumn("ImdbId").AsString().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("SortTitle").AsString().Nullable()
                .WithColumn("Year").AsInt32()
                .WithColumn("Path").AsString().Nullable()
                .WithColumn("QualityProfileId").AsInt32()
                .WithColumn("Monitored").AsBoolean()
                .WithColumn("ArrMovieFileId").AsInt32();

            CatalogFileTable("CatalogEpisodeFiles")
                .WithColumn("ArrSeriesId").AsInt32()
                .WithColumn("SeasonNumber").AsInt32();

            CatalogFileTable("CatalogMovieFiles")
                .WithColumn("ArrMovieId").AsInt32()
                .WithColumn("Edition").AsString().Nullable();

            Create.TableForModel("CatalogSyncStatus")
                .WithColumn("ApplicationId").AsInt32().Unique()
                .WithColumn("LastSyncStarted").AsDateTimeOffset().Nullable()
                .WithColumn("LastSyncCompleted").AsDateTimeOffset().Nullable()
                .WithColumn("LastSyncError").AsString().Nullable();

            foreach (var table in new[] { "CatalogQualityProfiles", "CatalogSeries", "CatalogEpisodes", "CatalogMovies", "CatalogEpisodeFiles", "CatalogMovieFiles" })
            {
                Create.Index().OnTable(table).OnColumn("ApplicationId").Ascending().OnColumn("ArrId").Ascending().WithOptions().Unique();
            }

            Create.Index().OnTable("CatalogEpisodes").OnColumn("ApplicationId").Ascending().OnColumn("ArrEpisodeFileId").Ascending();
        }

        private ICreateTableColumnOptionOrWithColumnSyntax CatalogTable(string name)
        {
            return Create.TableForModel(name)
                .WithColumn("ApplicationId").AsInt32()
                .WithColumn("ArrId").AsInt32()
                .WithColumn("MissingSince").AsDateTimeOffset().Nullable();
        }

        private ICreateTableColumnOptionOrWithColumnSyntax CatalogFileTable(string name)
        {
            return CatalogTable(name)
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
                .WithColumn("RunTime").AsString().Nullable();
        }
    }
}

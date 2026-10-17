using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(252)]
    public class add_get_it : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // What a friend's Arr apps know a title they sent by, the parts sent, and how adding it went.
            Alter.Table("ReceivedTitles")
                .AddColumn("Year").AsInt32().WithDefaultValue(0)
                .AddColumn("TvdbId").AsInt32().WithDefaultValue(0)
                .AddColumn("TmdbId").AsInt32().WithDefaultValue(0)
                .AddColumn("ImdbId").AsString().Nullable()
                .AddColumn("ForeignArtistId").AsString().Nullable()
                .AddColumn("IsAdult").AsBoolean().WithDefaultValue(false)
                .AddColumn("SeriesType").AsString().Nullable()
                .AddColumn("Parts").AsString().Nullable()
                .AddColumn("GetItState").AsInt32().WithDefaultValue(0)
                .AddColumn("GetItMessage").AsString().Nullable();
        }
    }
}

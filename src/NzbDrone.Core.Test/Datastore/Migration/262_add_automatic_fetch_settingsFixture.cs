using System.Data.SQLite;
using Dapper;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_automatic_fetch_settingsFixture : MigrationTest<add_automatic_fetch_settings>
    {
        [Test]
        public void existing_library_apps_should_await_their_arr_rss_choice_and_prowlarr_should_be_unchanged()
        {
            const string settings = "{\"baseUrl\":\"http://arr\",\"apiKey\":\"keep-key\",\"shareWithNewFriends\":false}";
            using var db = WithDapperMigrationTestDb(m =>
            {
                foreach (var implementation in new[] { "Sonarr", "Radarr", "Lidarr", "WhisparrV2", "WhisparrV3", "Prowlarr" })
                {
                    m.Insert.IntoTable("Applications").Row(new
                    {
                        Name = implementation, Implementation = implementation, ConfigContract = implementation + "Settings",
                        Settings = settings, SyncLevel = 1
                    });
                }
            });

            var rows = db.Query<(string Implementation, string Settings)>("SELECT [Implementation], [Settings] FROM [Applications]");
            foreach (var row in rows)
            {
                if (row.Implementation == "Prowlarr")
                {
                    row.Settings.Should().Be(settings);
                    continue;
                }

                var migrated = JObject.Parse(row.Settings);
                migrated["fetchNewFilesAutomatically"].Type.Should().Be(JTokenType.Null);
                migrated.Value<bool>("includeAutomaticUpgrades").Should().BeFalse();
                migrated.Value<string>("apiKey").Should().Be("keep-key");
                migrated.Value<bool>("shareWithNewFriends").Should().BeFalse();
                var converter = new EmbeddedDocumentConverter<SonarrSettings>();
                var pending = converter.Parse(row.Settings);
                pending.FetchNewFilesAutomatically.Should().BeNull();
                var parameter = new SQLiteParameter();
                new ProviderSettingConverter().SetValue(parameter, pending);
                converter.Parse(parameter.Value).FetchNewFilesAutomatically.Should().BeNull();
            }

            new SonarrSettings().FetchNewFilesAutomatically.Should().BeTrue();
            new SonarrSettings().IncludeAutomaticUpgrades.Should().BeFalse();
        }
    }
}

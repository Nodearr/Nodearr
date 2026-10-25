using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // What the Arr apps say of a file's revision, and of what they do with a later one.
    [TestFixture]
    public class QualityRevisionFixture : CoreTest<Sonarr>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ApplicationDefinition { Name = "Sonarr", Settings = new SonarrSettings { BaseUrl = "http://localhost:8989", ApiKey = "key" } };

            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.Get<List<ArrQualityProfileResource>>(It.IsAny<IArrConnection>(), "/qualityprofile"))
                  .Returns(new List<ArrQualityProfileResource> { new() { Id = 1, Name = "HD", UpgradeAllowed = true } });

            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.Get<List<SonarrSeriesResource>>(It.IsAny<IArrConnection>(), "/series"))
                  .Returns(new List<SonarrSeriesResource>());
        }

        private void GivenPropersAndRepacks(string setting)
        {
            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.Get<JObject>(It.IsAny<IArrConnection>(), "/config/mediamanagement"))
                  .Returns(JObject.FromObject(new { downloadPropersAndRepacks = setting }));
        }

        private bool UpgradesToPropers()
        {
            return QualityRanking.FromStored(Subject.FetchCatalog().QualityProfiles.Single().QualityRanking).UpgradeToPropers;
        }

        [Test]
        public void a_files_revision_should_be_read_as_the_arr_app_recorded_it()
        {
            var file = JsonConvert.DeserializeObject<SonarrEpisodeFileResource>(
                "{\"id\":12,\"relativePath\":\"a.mkv\",\"size\":5,\"quality\":{\"quality\":{\"name\":\"WEBDL-720p\"},\"revision\":{\"version\":2,\"real\":1,\"isRepack\":true}}}");

            file.ToCatalog().Should().Match<CatalogEpisodeFile>(f => f.RevisionVersion == 2 && f.RevisionReal == 1 && f.RevisionIsRepack);
        }

        [Test]
        public void a_file_with_no_revision_should_be_a_first_release()
        {
            var file = JsonConvert.DeserializeObject<SonarrEpisodeFileResource>("{\"id\":12,\"relativePath\":\"a.mkv\",\"size\":5,\"quality\":{\"quality\":{\"name\":\"WEBDL-720p\"}}}");

            file.ToCatalog().Should().Match<CatalogEpisodeFile>(f => f.RevisionVersion == 1 && f.RevisionReal == 0 && !f.RevisionIsRepack);
        }

        [TestCase(1, 0, false, null)]
        [TestCase(2, 0, false, "Proper")]
        [TestCase(2, 0, true, "Repack")]
        [TestCase(3, 0, true, "Repack 2")]
        [TestCase(1, 1, false, "Real")]
        [TestCase(2, 1, false, "Real Proper")]
        public void a_revision_should_be_shown_in_the_arr_apps_words(int version, int real, bool repack, string expected)
        {
            RevisionLabel.For(version, real, repack).Should().Be(expected);
        }

        [TestCase("preferAndUpgrade", true)]
        [TestCase("doNotUpgrade", false)]
        [TestCase("doNotPrefer", false)]
        public void the_profiles_should_know_what_the_app_does_with_a_later_revision(string setting, bool upgrades)
        {
            GivenPropersAndRepacks(setting);

            UpgradesToPropers().Should().Be(upgrades);
        }

        [Test]
        public void an_app_that_does_not_say_should_be_taken_to_have_its_default()
        {
            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.Get<JObject>(It.IsAny<IArrConnection>(), "/config/mediamanagement"))
                  .Throws(new InvalidOperationException("no such setting"));

            UpgradesToPropers().Should().BeTrue();
        }
    }
}

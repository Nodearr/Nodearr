using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Prowlarr;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Nodearr in Prowlarr: an indexer to search there, whose copies in Sonarr and Radarr are switched off.
    [TestFixture]
    public class ProwlarrRegistrationFixture : CoreTest<Prowlarr>
    {
        private JArray _profiles;
        private JArray _indexers;
        private List<(string Resource, JObject Body)> _posted;
        private List<(string Resource, JObject Body)> _put;
        private List<string> _deleted;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ApplicationDefinition
            {
                Name = "Prowlarr",
                SyncLevel = ApplicationSyncLevel.FullSync,
                Settings = new ProwlarrSettings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://prowlarr:9696", ApiKey = "prowlarr-key" }
            };

            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ApiKey).Returns("nodearr-key");

            _profiles = new JArray(new JObject { ["id"] = 1, ["name"] = "Standard", ["enableRss"] = true, ["enableAutomaticSearch"] = true, ["enableInteractiveSearch"] = true });
            _indexers = new JArray(new JObject { ["id"] = 4, ["implementation"] = "Newznab", ["name"] = "NZBgeek" });
            _posted = new List<(string, JObject)>();
            _put = new List<(string, JObject)>();
            _deleted = new List<string>();

            var proxy = Mocker.GetMock<IArrV3Proxy>();
            proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/appprofile")).Returns(() => _profiles);
            proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/indexer")).Returns(() => _indexers);
            proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/indexer/schema")).Returns(() => new JArray(
                Schema("Newznab", "NZBgeek", "https://api.nzbgeek.info"),
                Schema("Newznab", "Generic Newznab", ""),
                Schema("Torznab", "Generic Torznab", "")));
            proxy.Setup(p => p.Post(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                 .Returns<IArrConnection, string, JObject>((_, resource, body) =>
                 {
                     _posted.Add((resource, body));
                     return new JObject { ["id"] = resource == "/appprofile" ? 9 : 12 };
                 });
            proxy.Setup(p => p.Put(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                 .Returns<IArrConnection, string, JObject>((_, resource, body) =>
                 {
                     _put.Add((resource, body));
                     return body;
                 });
            proxy.Setup(p => p.Delete(It.IsAny<IArrConnection>(), It.IsAny<string>()))
                 .Callback<IArrConnection, string>((_, resource) => _deleted.Add(resource));
        }

        private static JObject Schema(string implementation, string name, string baseUrl)
        {
            return new JObject
            {
                ["id"] = 0,
                ["implementation"] = implementation,
                ["name"] = name,
                ["enable"] = false,
                ["appProfileId"] = 1,
                ["fields"] = new JArray(
                    new JObject { ["name"] = "baseUrl", ["value"] = baseUrl },
                    new JObject { ["name"] = "apiPath", ["value"] = "/api" },
                    new JObject { ["name"] = "apiKey", ["value"] = null })
            };
        }

        private static string Field(JObject provider, string name)
        {
            return provider["fields"].OfType<JObject>().First(f => f.Value<string>("name") == name).Value<string>("value");
        }

        [Test]
        public void the_sync_profile_should_switch_off_every_copy_prowlarr_makes()
        {
            Subject.SyncRegistration(false);

            var profile = _posted.Single(p => p.Resource == "/appprofile").Body;
            profile.Value<string>("name").Should().Be("Nodearr");
            profile.Value<bool>("enableRss").Should().BeFalse();
            profile.Value<bool>("enableAutomaticSearch").Should().BeFalse();
            profile.Value<bool>("enableInteractiveSearch").Should().BeFalse();
        }

        [Test]
        public void nodearr_should_be_added_as_a_generic_newznab_indexer_with_that_profile()
        {
            Subject.SyncRegistration(false);

            var indexer = _posted.Single(p => p.Resource == "/indexer").Body;
            indexer.Value<string>("name").Should().Be("Nodearr");
            indexer.Value<bool>("enable").Should().BeTrue();
            indexer.Value<int>("appProfileId").Should().Be(9);
            Field(indexer, "baseUrl").Should().Be("http://nodearr:6633/newznab");
            Field(indexer, "apiKey").Should().Be("nodearr-key");
        }

        [Test]
        public void nothing_should_be_written_when_prowlarr_already_has_it_all()
        {
            _profiles.Add(new JObject { ["id"] = 9, ["name"] = "Nodearr", ["enableRss"] = false, ["enableAutomaticSearch"] = false, ["enableInteractiveSearch"] = false, ["minimumSeeders"] = 1 });
            var indexer = Schema("Newznab", "Nodearr", "http://nodearr:6633/newznab");
            indexer["id"] = 12;
            indexer["enable"] = true;
            indexer["appProfileId"] = 9;
            _indexers.Add(indexer);

            Subject.SyncRegistration(false);

            _posted.Should().BeEmpty();
            _put.Should().BeEmpty();
        }

        [Test]
        public void a_profile_someone_switched_on_should_be_switched_off_again()
        {
            _profiles.Add(new JObject { ["id"] = 9, ["name"] = "Nodearr", ["enableRss"] = false, ["enableAutomaticSearch"] = true, ["enableInteractiveSearch"] = false, ["minimumSeeders"] = 1 });

            Subject.SyncRegistration(false);

            _put.Single(p => p.Resource == "/appprofile/9").Body.Value<bool>("enableAutomaticSearch").Should().BeFalse();
        }

        [Test]
        public void removing_should_take_out_the_indexer_and_the_profile_and_nothing_else()
        {
            _profiles.Add(new JObject { ["id"] = 9, ["name"] = "Nodearr" });
            _indexers.Add(new JObject { ["id"] = 12, ["implementation"] = "Newznab", ["name"] = "Nodearr" });

            Subject.RemoveRegistration();

            _deleted.Should().Equal("/indexer/12", "/appprofile/9");
        }

        [Test]
        public void prowlarr_should_hold_no_library()
        {
            ((ApplicationDefinition)Subject.Definition).Implementation = nameof(Prowlarr);

            ((ApplicationDefinition)Subject.Definition).HoldsLibrary.Should().BeFalse();
            Subject.FetchCatalog().Series.Should().BeEmpty();
        }
    }
}

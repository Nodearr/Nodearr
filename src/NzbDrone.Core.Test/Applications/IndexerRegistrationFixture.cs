using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    [TestFixture]
    public class IndexerRegistrationFixture : CoreTest<Sonarr>
    {
        private readonly Dictionary<string, JArray> _present = new();
        private readonly List<(string Resource, JObject Body)> _posted = new();
        private readonly List<(string Resource, JObject Body)> _put = new();

        [SetUp]
        public void Setup()
        {
            _present.Clear();
            _posted.Clear();
            _put.Clear();
            Subject.Definition = new ApplicationDefinition
            {
                Id = 12,
                Name = "Sonarr",
                SyncLevel = ApplicationSyncLevel.FullSync,
                Settings = new SonarrSettings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://sonarr:8989" }
            };
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ApiKey).Returns("nodearr-key");
            var proxy = Mocker.GetMock<IArrV3Proxy>();
            proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), It.IsAny<string>()))
                .Returns<IArrConnection, string>((_, resource) => resource.EndsWith("/schema")
                    ? new JArray(new JObject { ["implementation"] = resource == "/downloadclient/schema" ? "Sabnzbd" : resource == "/indexer/schema" ? "Newznab" : "Webhook", ["enableRss"] = resource == "/indexer/schema", ["fields"] = new JArray(new JObject { ["name"] = "baseUrl" }) })
                    : _present.GetValueOrDefault(resource) ?? new JArray());
            proxy.Setup(p => p.Post(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                .Returns<IArrConnection, string, JObject>((_, resource, body) =>
                {
                    body["id"] = _posted.Count + 1;
                    _posted.Add((resource, (JObject)body.DeepClone()));
                    return body;
                });
            proxy.Setup(p => p.Put(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                .Returns<IArrConnection, string, JObject>((_, resource, body) =>
                {
                    _put.Add((resource, (JObject)body.DeepClone()));
                    return body;
                });
        }

        [Test]
        public void new_indexer_should_start_with_rss_off_and_searches_pinned_to_the_download_client()
        {
            Subject.SyncRegistration(false);

            var client = _posted.Single(p => p.Resource == "/downloadclient").Body;
            var indexer = _posted.Single(p => p.Resource == "/indexer").Body;
            indexer["enableRss"].Should().NotBeNull();
            indexer.Value<bool>("enableRss").Should().BeFalse();
            indexer.Value<bool>("enableAutomaticSearch").Should().BeTrue();
            indexer.Value<bool>("enableInteractiveSearch").Should().BeTrue();
            indexer.Value<int>("downloadClientId").Should().Be(client.Value<int>("id"));
            indexer["fields"].Single(f => f.Value<string>("name") == "baseUrl").Value<string>("value").Should().Be("http://nodearr:6633/newznab/12");
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void existing_indexer_should_keep_rss_and_restore_searches_and_the_pin(bool rss, bool force)
        {
            Subject.SyncRegistration(false);
            foreach (var item in _posted)
            {
                _present[item.Resource] = new JArray(item.Body.DeepClone());
            }

            var indexer = (JObject)_present["/indexer"][0];
            indexer["enableRss"] = rss;
            indexer["enableAutomaticSearch"] = false;
            indexer["enableInteractiveSearch"] = false;
            indexer["downloadClientId"] = 999;
            indexer["fields"].Single(f => f.Value<string>("name") == "baseUrl")["value"] = "http://nodearr:6633/newznab";
            _put.Clear();

            Subject.SyncRegistration(force);

            var updated = _put.Single(p => p.Resource == $"/indexer/{indexer.Value<int>("id")}").Body;
            updated.Value<bool>("enableRss").Should().Be(rss);
            updated.Value<bool>("enableAutomaticSearch").Should().BeTrue();
            updated.Value<bool>("enableInteractiveSearch").Should().BeTrue();
            updated.Value<int>("downloadClientId").Should().Be(_present["/downloadclient"][0].Value<int>("id"));
            updated["fields"].Single(f => f.Value<string>("name") == "baseUrl").Value<string>("value").Should().Be("http://nodearr:6633/newznab/12");
        }

        [Test]
        public void changing_only_rss_should_not_trigger_an_indexer_update()
        {
            Subject.SyncRegistration(false);
            foreach (var item in _posted)
            {
                _present[item.Resource] = new JArray(item.Body.DeepClone());
            }

            _present["/indexer"][0]["enableRss"] = true;
            _put.Clear();

            Subject.SyncRegistration(false);

            _put.Should().NotContain(p => p.Resource.StartsWith("/indexer/"));
            _present["/indexer"][0].Value<bool>("enableRss").Should().BeTrue();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void legacy_add_only_registration_should_receive_the_safe_url_and_keep_its_switches(bool rss)
        {
            Subject.SyncRegistration(false);
            foreach (var item in _posted)
            {
                _present[item.Resource] = new JArray(item.Body.DeepClone());
            }

            var indexer = (JObject)_present["/indexer"][0];
            indexer["fields"].Single(f => f.Value<string>("name") == "baseUrl")["value"] = "http://nodearr:6633/newznab";
            indexer["enableRss"] = rss;
            indexer["enableAutomaticSearch"] = false;
            indexer["enableInteractiveSearch"] = false;
            indexer["downloadClientId"] = 999;
            ((ApplicationDefinition)Subject.Definition).SyncLevel = ApplicationSyncLevel.AddOnly;
            _put.Clear();

            Subject.SyncRegistration(false);
            var updated = _put.Single().Body;
            updated["fields"].Single(f => f.Value<string>("name") == "baseUrl").Value<string>("value").Should().Be("http://nodearr:6633/newznab/12");
            updated.Value<bool>("enableRss").Should().Be(rss);
            updated.Value<bool>("enableAutomaticSearch").Should().BeFalse();
            updated.Value<bool>("enableInteractiveSearch").Should().BeFalse();
            updated.Value<int>("downloadClientId").Should().Be(999);

            _put.Clear();
            Subject.SyncRegistration(false);
            _put.Should().BeEmpty();
        }
    }
}

using System;
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
        public void new_indexer_should_start_with_rss_on_and_searches_pinned_to_the_download_client()
        {
            Subject.SyncRegistration(false);

            var client = _posted.Single(p => p.Resource == "/downloadclient").Body;
            var indexer = _posted.Single(p => p.Resource == "/indexer").Body;
            indexer["enableRss"].Should().NotBeNull();
            indexer.Value<bool>("enableRss").Should().BeTrue();
            indexer.Value<bool>("enableAutomaticSearch").Should().BeTrue();
            indexer.Value<bool>("enableInteractiveSearch").Should().BeTrue();
            indexer.Value<int>("downloadClientId").Should().Be(client.Value<int>("id"));
            indexer["fields"].Single(f => f.Value<string>("name") == "baseUrl").Value<string>("value").Should().Be("http://nodearr:6633/newznab/12");
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void existing_indexer_should_apply_the_nodearr_rss_choice_and_restore_searches_and_the_pin(bool rss, bool force)
        {
            Subject.SyncRegistration(false);
            foreach (var item in _posted)
            {
                _present[item.Resource] = new JArray(item.Body.DeepClone());
            }

            var indexer = (JObject)_present["/indexer"][0];
            indexer["enableRss"] = !rss;
            ((SonarrSettings)Subject.Definition.Settings).FetchNewFilesAutomatically = rss;
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
        public void a_matching_rss_choice_should_not_trigger_an_indexer_update()
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
            ((SonarrSettings)Subject.Definition.Settings).FetchNewFilesAutomatically = rss;
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

            ((SonarrSettings)Subject.Definition.Settings).FetchNewFilesAutomatically = !rss;
            Subject.SyncRegistration(false);
            _put.Single().Body.Value<bool>("enableRss").Should().Be(!rss);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void migration_should_read_and_save_the_existing_rss_choice_before_sync(bool rss)
        {
            Subject.SyncRegistration(false);
            foreach (var item in _posted)
            {
                _present[item.Resource] = new JArray(item.Body.DeepClone());
            }

            _present["/indexer"][0]["enableRss"] = rss;
            var settings = (SonarrSettings)Subject.Definition.Settings;
            settings.FetchNewFilesAutomatically = null;
            Subject.SyncRegistration(false);

            settings.FetchNewFilesAutomatically.Should().Be(rss);
            settings.IncludeAutomaticUpgrades.Should().BeFalse();
            Mocker.GetMock<IApplicationsRepository>().Verify(r => r.UpdateSettings(It.Is<ApplicationDefinition>(a => ((SonarrSettings)a.Settings).FetchNewFilesAutomatically == rss)), Times.Once());
            Subject.SyncRegistration(false);
            Mocker.GetMock<IApplicationsRepository>().Verify(r => r.UpdateSettings(It.IsAny<ApplicationDefinition>()), Times.Once());
        }

        [Test]
        public void migration_without_an_existing_indexer_should_save_the_new_default()
        {
            var settings = (SonarrSettings)Subject.Definition.Settings;
            settings.FetchNewFilesAutomatically = null;
            Subject.SyncRegistration(false);
            settings.FetchNewFilesAutomatically.Should().BeTrue();
            Mocker.GetMock<IApplicationsRepository>().Verify(r => r.UpdateSettings(It.IsAny<ApplicationDefinition>()), Times.Once());
            _posted.Single(p => p.Resource == "/indexer").Body.Value<bool>("enableRss").Should().BeTrue();
        }

        [Test]
        public void an_unreachable_legacy_app_should_remain_pending_without_registration_writes()
        {
            var settings = (SonarrSettings)Subject.Definition.Settings;
            settings.FetchNewFilesAutomatically = null;
            Mocker.GetMock<IArrV3Proxy>().Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/indexer")).Throws(new InvalidOperationException("offline"));
            Subject.Invoking(s => s.SyncRegistration(false)).Should().Throw<InvalidOperationException>();
            settings.FetchNewFilesAutomatically.Should().BeNull();
            _posted.Should().BeEmpty();
            _put.Should().BeEmpty();
            Mocker.GetMock<IApplicationsRepository>().Verify(r => r.UpdateSettings(It.IsAny<ApplicationDefinition>()), Times.Never());
        }

        [Test]
        public void a_failed_local_save_should_remain_pending_without_registration_writes()
        {
            var settings = (SonarrSettings)Subject.Definition.Settings;
            settings.FetchNewFilesAutomatically = null;
            Mocker.GetMock<IApplicationsRepository>().Setup(r => r.UpdateSettings(It.IsAny<ApplicationDefinition>())).Throws(new InvalidOperationException("save failed"));
            Subject.Invoking(s => s.SyncRegistration(false)).Should().Throw<InvalidOperationException>();
            settings.FetchNewFilesAutomatically.Should().BeNull();
            _posted.Should().BeEmpty();
            _put.Should().BeEmpty();
        }
    }
}

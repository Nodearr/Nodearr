using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Radarr;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Nodearr adds the Arr app's own webhook connection, so the app says when its files change.
    [TestFixture]
    public class WebhookRegistrationFixture : CoreTest
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

            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ApiKey).Returns("nodearr-key");

            var proxy = Mocker.GetMock<IArrV3Proxy>();

            proxy.Setup(p => p.GetArray(It.IsAny<ArrApplicationSettings>(), It.IsAny<string>()))
                 .Returns<ArrApplicationSettings, string>((_, resource) => resource.EndsWith("/schema") ? Schema(resource) : _present.GetValueOrDefault(resource) ?? new JArray());
            proxy.Setup(p => p.Post(It.IsAny<ArrApplicationSettings>(), It.IsAny<string>(), It.IsAny<JObject>()))
                 .Returns<ArrApplicationSettings, string, JObject>((_, resource, body) =>
                 {
                     _posted.Add((resource, body));
                     body["id"] = _posted.Count;

                     return body;
                 });
            proxy.Setup(p => p.Put(It.IsAny<ArrApplicationSettings>(), It.IsAny<string>(), It.IsAny<JObject>()))
                 .Returns<ArrApplicationSettings, string, JObject>((_, resource, body) =>
                 {
                     _put.Add((resource, body));

                     return body;
                 });
        }

        // What an Arr app's schema endpoint answers: one provider of each kind Nodearr adds, with
        // the fields it fills in.
        private static JArray Schema(string resource)
        {
            var (implementation, fields) = resource switch
            {
                "/downloadclient/schema" => ("Sabnzbd", new[] { "host", "port", "useSsl", "urlBase", "apiKey", "tvCategory", "movieCategory" }),
                "/indexer/schema" => ("Newznab", new[] { "baseUrl", "apiPath", "apiKey", "categories", "animeCategories", "animeStandardFormatSearch" }),
                _ => ("Webhook", new[] { "url", "method", "username", "password", "headers" })
            };

            return new JArray(new JObject
            {
                ["implementation"] = implementation,
                ["fields"] = new JArray(fields.Select(f => new JObject { ["name"] = f, ["value"] = null }))
            });
        }

        private T Given<T>(int id)
            where T : IApplication
        {
            var application = Mocker.Resolve<T>();
            ArrApplicationSettings settings = typeof(T) == typeof(Sonarr) ? new SonarrSettings() : new RadarrSettings();

            settings.NodearrUrl = "http://nodearr:6633";
            settings.BaseUrl = "http://arr:8989";
            application.Definition = new ApplicationDefinition { Id = id, Name = typeof(T).Name, SyncLevel = ApplicationSyncLevel.FullSync, Settings = settings };

            return application;
        }

        private JObject PostedWebhook => _posted.Single(p => p.Resource == "/notification").Body;

        private static JToken Field(JObject provider, string name)
        {
            return provider["fields"].OfType<JObject>().Single(f => f.Value<string>("name") == name)["value"];
        }

        [Test]
        public void sonarr_should_be_given_a_webhook_to_this_node_that_carries_the_api_key()
        {
            Given<Sonarr>(3).SyncRegistration(false);

            var webhook = PostedWebhook;

            webhook.Value<string>("name").Should().Be("Nodearr");
            Field(webhook, "url").Value<string>().Should().Be("http://nodearr:6633/api/v5/arr/webhook/3");
            Field(webhook, "method").Value<int>().Should().Be(1);
            Field(webhook, "headers").Should().BeEquivalentTo(new JArray(new JObject { ["key"] = "X-Api-Key", ["value"] = "nodearr-key" }));
        }

        [Test]
        public void sonarr_should_say_when_files_or_series_come_and_go_and_nothing_else()
        {
            Given<Sonarr>(3).SyncRegistration(false);

            var webhook = PostedWebhook;
            var on = new[] { "onDownload", "onUpgrade", "onRename", "onSeriesAdd", "onSeriesDelete", "onEpisodeFileDelete", "onEpisodeFileDeleteForUpgrade" };
            var off = new[] { "onGrab", "onImportComplete", "onHealthIssue", "onHealthRestored", "onApplicationUpdate", "onManualInteractionRequired" };

            on.Should().OnlyContain(e => webhook.Value<bool>(e));
            off.Should().OnlyContain(e => !webhook.Value<bool>(e));
        }

        [Test]
        public void radarr_should_say_when_files_or_movies_come_and_go_by_its_own_names()
        {
            Given<Radarr>(4).SyncRegistration(false);

            var webhook = PostedWebhook;

            Field(webhook, "url").Value<string>().Should().Be("http://nodearr:6633/api/v5/arr/webhook/4");
            new[] { "onDownload", "onUpgrade", "onRename", "onMovieAdded", "onMovieDelete", "onMovieFileDelete", "onMovieFileDeleteForUpgrade" }.Should().OnlyContain(e => webhook.Value<bool>(e));
            webhook.Value<bool>("onGrab").Should().BeFalse();
        }

        [Test]
        public void a_webhook_already_as_it_should_be_should_be_left_alone()
        {
            var sonarr = Given<Sonarr>(3);

            sonarr.SyncRegistration(false);
            _present["/notification"] = new JArray(PostedWebhook);
            _posted.Clear();

            sonarr.SyncRegistration(false);

            _posted.Should().NotContain(p => p.Resource == "/notification");
            _put.Should().NotContain(p => p.Resource.StartsWith("/notification"));
        }

        [Test]
        public void a_webhook_pointing_somewhere_else_should_be_put_right()
        {
            var sonarr = Given<Sonarr>(3);

            sonarr.SyncRegistration(false);

            var stale = (JObject)PostedWebhook.DeepClone();

            stale["fields"].OfType<JObject>().Single(f => f.Value<string>("name") == "url")["value"] = "http://old-address:6633/api/v5/arr/webhook/3";
            _present["/notification"] = new JArray(stale);
            _posted.Clear();

            sonarr.SyncRegistration(false);

            Field(_put.Single(p => p.Resource.StartsWith("/notification")).Body, "url").Value<string>().Should().Be("http://nodearr:6633/api/v5/arr/webhook/3");
        }
    }
}

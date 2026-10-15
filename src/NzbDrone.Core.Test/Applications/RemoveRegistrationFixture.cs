using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Takes Nodearr back out of an Arr app, and touches nothing else that is set up there.
    [TestFixture]
    public class RemoveRegistrationFixture : CoreTest<Sonarr>
    {
        private List<string> _deleted;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ApplicationDefinition
            {
                Name = "Sonarr",
                Settings = new SonarrSettings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://sonarr:8989" }
            };

            _deleted = new List<string>();

            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.Delete(It.IsAny<IArrConnection>(), It.IsAny<string>()))
                  .Callback<IArrConnection, string>((_, resource) => _deleted.Add(resource));

            GivenTheAppHas("/indexer", (1, "Newznab", "NZBgeek"), (2, "Newznab", "Nodearr"), (3, "Torznab", "Nodearr"));
            GivenTheAppHas("/downloadclient", (7, "Sabnzbd", "Nodearr"), (8, "Sabnzbd", "SABnzbd"), (9, "QBittorrent", "Nodearr"));
            GivenTheAppHas("/notification", (4, "Webhook", "Nodearr"), (5, "Webhook", "Home Assistant"), (6, "Discord", "Nodearr"));
        }

        private void GivenTheAppHas(string resource, params (int Id, string Implementation, string Name)[] providers)
        {
            var array = new JArray();

            foreach (var provider in providers)
            {
                array.Add(new JObject { ["id"] = provider.Id, ["implementation"] = provider.Implementation, ["name"] = provider.Name });
            }

            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.GetArray(It.IsAny<IArrConnection>(), resource))
                  .Returns(array);
        }

        [Test]
        public void the_webhook_should_go_first_then_the_indexer_and_then_the_download_client_it_points_at()
        {
            Subject.RemoveRegistration();

            _deleted.Should().Equal("/notification/4", "/indexer/2", "/downloadclient/7");
        }

        [Test]
        public void nothing_should_be_deleted_from_an_app_nodearr_is_not_in()
        {
            GivenTheAppHas("/indexer", (1, "Newznab", "NZBgeek"));
            GivenTheAppHas("/downloadclient", (8, "Sabnzbd", "SABnzbd"));
            GivenTheAppHas("/notification", (5, "Webhook", "Home Assistant"));

            Subject.RemoveRegistration();

            _deleted.Should().BeEmpty();
        }

        [Test]
        public void an_app_that_cannot_be_reached_should_say_so_to_the_caller()
        {
            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/notification"))
                  .Throws(new InvalidOperationException("connection refused"));

            Assert.Throws<InvalidOperationException>(() => Subject.RemoveRegistration());
            _deleted.Should().BeEmpty();
        }
    }
}

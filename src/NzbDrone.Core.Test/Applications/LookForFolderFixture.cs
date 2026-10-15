using System;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Works out where an Arr app would look for one of Nodearr's folders, and asks it.
    [TestFixture]
    public class LookForFolderFixture : CoreTest<Sonarr>
    {
        private const string Marker = "nodearr-check-1234abcd";

        private string _asked;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ApplicationDefinition
            {
                Name = "Sonarr",
                Settings = new SonarrSettings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://sonarr:8989" }
            };

            GivenMappings();
            GivenTheAppLists(Marker);
        }

        private void GivenMappings(params (string Host, string Remote, string Local)[] mappings)
        {
            var array = new JArray();

            foreach (var mapping in mappings)
            {
                array.Add(new JObject { ["host"] = mapping.Host, ["remotePath"] = mapping.Remote, ["localPath"] = mapping.Local });
            }

            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.GetArray(It.IsAny<ArrApplicationSettings>(), "/remotepathmapping"))
                  .Returns(array);
        }

        private void GivenTheAppLists(params string[] directories)
        {
            var listing = new JObject { ["directories"] = new JArray() };

            foreach (var directory in directories)
            {
                ((JArray)listing["directories"]).Add(new JObject { ["name"] = directory });
            }

            Mocker.GetMock<IArrV3Proxy>()
                  .Setup(p => p.Get<JObject>(It.IsAny<ArrApplicationSettings>(), It.Is<string>(r => r.StartsWith("/filesystem?"))))
                  .Callback<ArrApplicationSettings, string>((_, resource) => _asked = Uri.UnescapeDataString(resource))
                  .Returns(listing);
        }

        [Test]
        public void with_no_mapping_the_app_is_asked_about_the_same_path()
        {
            var seen = Subject.LookForFolder("/downloads/complete", Marker);

            seen.Found.Should().BeTrue();
            seen.LookedAt.Should().Be("/downloads/complete");
            _asked.Should().StartWith("/filesystem?path=/downloads/complete&");
        }

        [Test]
        public void the_marker_not_being_listed_means_the_app_does_not_see_the_folder()
        {
            GivenTheAppLists("tv", "movies");

            Subject.LookForFolder("/downloads/complete", Marker).Found.Should().BeFalse();
        }

        [Test]
        public void the_apps_own_mapping_for_nodearr_decides_where_it_looks()
        {
            GivenMappings(("nodearr", @"C:\Nodearr\downloads\complete\", "/downloads/complete/"));

            var seen = Subject.LookForFolder(@"C:\Nodearr\downloads\complete", Marker);

            seen.LookedAt.Should().Be("/downloads/complete/");
            _asked.Should().StartWith("/filesystem?path=/downloads/complete/&");
        }

        [Test]
        public void a_mapping_for_a_parent_folder_should_carry_the_rest_of_the_path()
        {
            GivenMappings(("NODEARR", "/data/nodearr/", "/mnt/shared/"));

            Subject.LookForFolder("/data/nodearr/complete", Marker).LookedAt.Should().Be("/mnt/shared/complete/");
        }

        [Test]
        public void a_mapping_for_another_download_client_should_be_ignored()
        {
            GivenMappings(("sabnzbd", "/downloads/complete/", "/somewhere/else/"));

            Subject.LookForFolder("/downloads/complete", Marker).LookedAt.Should().Be("/downloads/complete");
        }
    }
}

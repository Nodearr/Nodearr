using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck
{
    [TestFixture]
    public class HealthCheckFixture : CoreTest
    {
        private const string Guide = "https://nodearr.com/guide/troubleshooting/";

        [TestCase("I blew up because of some weird user mistake", null, Guide + "#i-blew-up-because-of-some-weird-user-mistake")]
        [TestCase("I blew up because of some weird user mistake", "#my-health-check", Guide + "#my-health-check")]
        public void should_link_to_the_troubleshooting_guide(string message, string wikiFragment, string expectedUrl)
        {
            var subject = new NzbDrone.Core.HealthCheck.HealthCheck(typeof(HealthCheckBase), HealthCheckResult.Warning, HealthCheckReason.ServerNotification, message, wikiFragment);

            subject.WikiUrl.Should().Be(expectedUrl);
        }

        // Each of Nodearr's own checks leads to its section of the guide, whatever its message says.
        [TestCase(HealthCheckReason.LibraryNotReadable, "#friends-are-refused-your-files")]
        [TestCase(HealthCheckReason.DownloadFolderNotShared, "#the-arr-app-does-not-import-what-arrived")]
        [TestCase(HealthCheckReason.PeerRelayOnly, "#relay-only")]
        [TestCase(HealthCheckReason.PairAttempts, "#pairing-keeps-being-turned-away")]
        public void a_nodearr_check_should_link_to_its_section_of_the_guide(HealthCheckReason reason, string section)
        {
            var subject = new NzbDrone.Core.HealthCheck.HealthCheck(typeof(HealthCheckBase), HealthCheckResult.Warning, reason, "Anything at all");

            subject.WikiUrl.Should().Be(Guide + section);
        }
    }
}

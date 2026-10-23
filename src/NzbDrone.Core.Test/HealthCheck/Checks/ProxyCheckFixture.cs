using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class ProxyCheckFixture : CoreTest<ProxyCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Returns((string key, Dictionary<string, object> tokens) => $"{key}: {tokens["proxyHostName"]}");
        }

        private void GivenProxy(bool enabled, string hostname)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ProxyEnabled).Returns(enabled);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ProxyHostname).Returns(hostname);
        }

        [Test]
        public void should_be_ok_when_no_proxy_is_used()
        {
            GivenProxy(false, "nodearr-test.invalid");

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_be_ok_when_the_proxy_name_resolves()
        {
            GivenProxy(true, "localhost");

            Subject.Check().ShouldBeOk();
        }

        // .invalid never resolves (RFC 6761), offline or not. The check no longer reaches past the
        // name to a services host, so there is nothing else for it to fail on.
        [Test]
        public void should_be_error_when_the_proxy_name_does_not_resolve()
        {
            GivenProxy(true, "nodearr-test.invalid");

            Subject.Check().ShouldBeError("ProxyResolveIpHealthCheckMessage: nodearr-test.invalid", "proxy-failed-resolve-ip");
        }
    }
}

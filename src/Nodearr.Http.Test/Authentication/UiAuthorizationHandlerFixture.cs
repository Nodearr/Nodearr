using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Http.Authentication;
using NzbDrone.Test.Common;

namespace Nodearr.Http.Test.Authentication
{
    [TestFixture]
    public class UiAuthorizationHandlerFixture : TestBase<UiAuthorizationHandler>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.AuthenticationRequired)
                  .Returns(AuthenticationRequiredType.DisabledForLocalAddresses);
        }

        private static HttpContext GetHttpContext(string remoteIp, string unconsumedForwardedFor)
        {
            var httpContext = new DefaultHttpContext();

            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

            if (unconsumedForwardedFor != null)
            {
                httpContext.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName] = unconsumedForwardedFor;
            }

            return httpContext;
        }

        private bool IsAuthorized(HttpContext httpContext)
        {
            var requirement = new BypassableDenyAnonymousAuthorizationRequirement();
            var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), httpContext);

            Subject.HandleAsync(context).GetAwaiter().GetResult();

            return context.HasSucceeded;
        }

        [TestCase("127.0.0.1")]
        [TestCase("192.168.1.50")]
        [TestCase("10.0.0.5")]
        public void should_bypass_authentication_for_local_address_without_forwarded_headers(string address)
        {
            IsAuthorized(GetHttpContext(address, null)).Should().BeTrue();
        }

        [Test]
        public void should_not_bypass_authentication_for_public_address_without_forwarded_headers()
        {
            IsAuthorized(GetHttpContext("203.0.113.66", null)).Should().BeFalse();
        }

        [Test]
        public void should_bypass_authentication_when_trusted_proxy_resolved_a_local_client()
        {
            IsAuthorized(GetHttpContext("192.168.1.50", null)).Should().BeTrue();
        }

        [Test]
        public void should_not_bypass_authentication_when_trusted_proxy_resolved_a_public_client()
        {
            IsAuthorized(GetHttpContext("203.0.113.66", null)).Should().BeFalse();
        }

        [TestCase("127.0.0.1")]
        [TestCase("192.168.1.50")]
        [TestCase("10.0.0.5")]
        public void should_not_bypass_authentication_for_forwarded_headers_from_an_untrusted_peer(string forwardedFor)
        {
            IsAuthorized(GetHttpContext("10.0.0.99", forwardedFor)).Should().BeFalse();
        }

        [Test]
        public void should_not_bypass_authentication_for_a_truncated_proxy_chain()
        {
            IsAuthorized(GetHttpContext("10.0.0.2", "203.0.113.9")).Should().BeFalse();
        }

        [Test]
        public void should_bypass_authentication_for_loopback_when_disabled_for_localhost()
        {
            GivenAuthenticationRequired(AuthenticationRequiredType.DisabledForLocalhost);

            IsAuthorized(GetHttpContext("127.0.0.1", null)).Should().BeTrue();
        }

        [TestCase("192.168.1.50")]
        [TestCase("172.17.0.1")]
        public void should_not_bypass_authentication_for_another_machine_when_disabled_for_localhost(string address)
        {
            GivenAuthenticationRequired(AuthenticationRequiredType.DisabledForLocalhost);

            IsAuthorized(GetHttpContext(address, null)).Should().BeFalse();
        }

        // A device on the network claimed to be this machine, and the claim was not believed: the
        // header is still there, so the address is not known to be the client's.
        [TestCase("192.168.1.50")]
        [TestCase("127.0.0.1")]
        public void should_not_bypass_authentication_for_localhost_with_a_forwarded_address_left_unconsumed(string remoteIp)
        {
            GivenAuthenticationRequired(AuthenticationRequiredType.DisabledForLocalhost);

            IsAuthorized(GetHttpContext(remoteIp, "127.0.0.1")).Should().BeFalse();
        }

        private void GivenAuthenticationRequired(AuthenticationRequiredType type)
        {
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.AuthenticationRequired)
                  .Returns(type);
        }

        [Test]
        public void should_not_bypass_authentication_when_authentication_is_required()
        {
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.AuthenticationRequired)
                  .Returns(AuthenticationRequiredType.Enabled);

            IsAuthorized(GetHttpContext("127.0.0.1", null)).Should().BeFalse();
        }
    }
}

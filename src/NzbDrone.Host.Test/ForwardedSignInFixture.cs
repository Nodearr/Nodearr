using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Host;
using NzbDrone.Http.Authentication;

namespace NzbDrone.App.Test
{
    // The forwarded-headers middleware and the sign-in check together, as a request meets them.
    // Every private network used to be trusted as a proxy, so a device on the network could send
    // X-Forwarded-For: 127.0.0.1 and be let in where sign-in was turned off for localhost.
    [TestFixture]
    public class ForwardedSignInFixture
    {
        private static async Task<bool> IsLetIn(AuthenticationRequiredType required, string trustedNetworks, string remoteIp, string forwardedFor)
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.TrustedNetworks).Returns(trustedNetworks);
            config.SetupGet(c => c.AuthenticationRequired).Returns(required);

            var options = new ForwardedHeadersOptions();
            ForwardedHeadersConfigurator.Configure(options, config.Object);

            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            httpContext.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName] = forwardedFor;

            var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
            await middleware.Invoke(httpContext);

            var requirement = new BypassableDenyAnonymousAuthorizationRequirement();
            var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), httpContext);
            await new UiAuthorizationHandler(config.Object).HandleAsync(context);

            return context.HasSucceeded;
        }

        [TestCase("192.168.86.50")]
        [TestCase("10.1.2.3")]
        [TestCase("172.17.0.1")]
        public async Task a_device_on_the_network_claiming_to_be_this_machine_should_not_be_let_in(string remoteIp)
        {
            (await IsLetIn(AuthenticationRequiredType.DisabledForLocalhost, string.Empty, remoteIp, "127.0.0.1")).Should().BeFalse();
        }

        // Through Docker's default bridge every connection, from outside the host as well, seems to
        // come from the bridge's gateway.
        [Test]
        public async Task a_request_through_the_docker_bridge_claiming_a_local_address_should_not_be_let_in()
        {
            (await IsLetIn(AuthenticationRequiredType.DisabledForLocalAddresses, string.Empty, "172.17.0.1", "203.0.113.24")).Should().BeFalse();
        }

        [Test]
        public async Task a_reverse_proxy_on_this_machine_should_still_have_its_client_believed()
        {
            (await IsLetIn(AuthenticationRequiredType.DisabledForLocalAddresses, string.Empty, "127.0.0.1", "192.168.86.50")).Should().BeTrue();
            (await IsLetIn(AuthenticationRequiredType.DisabledForLocalAddresses, string.Empty, "127.0.0.1", "203.0.113.24")).Should().BeFalse();
        }

        [Test]
        public async Task a_reverse_proxy_on_a_trusted_network_should_have_its_client_believed()
        {
            (await IsLetIn(AuthenticationRequiredType.DisabledForLocalAddresses, "172.17.0.1", "172.17.0.1", "192.168.86.50")).Should().BeTrue();
            (await IsLetIn(AuthenticationRequiredType.DisabledForLocalAddresses, "172.17.0.1", "172.17.0.1", "203.0.113.24")).Should().BeFalse();
        }
    }
}

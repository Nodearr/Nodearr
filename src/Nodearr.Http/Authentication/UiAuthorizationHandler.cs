using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Nodearr.Http.Extensions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Http.Authentication
{
    public class UiAuthorizationHandler : AuthorizationHandler<BypassableDenyAnonymousAuthorizationRequirement>, IAuthorizationRequirement, IHandle<ConfigSavedEvent>
    {
        private readonly IConfigFileProvider _configService;
        private static AuthenticationRequiredType _authenticationRequired;

        public UiAuthorizationHandler(IConfigFileProvider configService)
        {
            _configService = configService;
            _authenticationRequired = configService.AuthenticationRequired;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BypassableDenyAnonymousAuthorizationRequirement requirement)
        {
            if (_authenticationRequired == AuthenticationRequiredType.DisabledForLocalAddresses)
            {
                if (context.Resource is HttpContext httpContext &&
                    IPAddress.TryParse(httpContext.GetRemoteIP(), out var ipAddress) &&
                    IsClientAddressKnown(httpContext))
                {
                    if (ipAddress.IsLocalAddress() ||
                        (_configService.TrustCgnatIpAddresses && ipAddress.IsCgnatIpAddress()))
                    {
                        context.Succeed(requirement);
                    }
                }
            }
            else if (_authenticationRequired == AuthenticationRequiredType.DisabledForLocalhost)
            {
                if (context.Resource is HttpContext httpContext &&
                    IPAddress.TryParse(httpContext.GetRemoteIP(), out var ipAddress) &&
                    IsClientAddressKnown(httpContext) &&
                    IPAddress.IsLoopback(ipAddress))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }

        // A forwarded address left in the request came from a sender that is not a trusted proxy, or
        // past the end of the chain of trusted ones, so the address the request seems to come from
        // is not known to be the client's.
        private static bool IsClientAddressKnown(HttpContext httpContext)
        {
            return !httpContext.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XForwardedForHeaderName);
        }

        public void Handle(ConfigSavedEvent message)
        {
            _authenticationRequired = _configService.AuthenticationRequired;
        }
    }
}

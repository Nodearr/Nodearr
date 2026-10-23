using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Sonarr's check also pinged its services host through the proxy. Nodearr has no such host, so
    // only the proxy's name is checked: a ping to a host that serves nothing would always fail.
    [CheckOn(typeof(ConfigSavedEvent))]
    public class ProxyCheck : HealthCheckBase
    {
        private readonly Logger _logger;
        private readonly IConfigService _configService;

        public ProxyCheck(IConfigService configService, Logger logger, ILocalizationService localizationService)
            : base(localizationService)
        {
            _configService = configService;
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            if (!_configService.ProxyEnabled)
            {
                return new HealthCheck(GetType());
            }

            IPAddress[] addresses;

            try
            {
                addresses = Dns.GetHostAddresses(_configService.ProxyHostname);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                _logger.Debug(ex, "Proxy hostname {0} did not resolve", _configService.ProxyHostname);
                addresses = [];
            }

            if (!addresses.Any())
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Error,
                    HealthCheckReason.ProxyResolveIp,
                    _localizationService.GetLocalizedString("ProxyResolveIpHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "proxyHostName", _configService.ProxyHostname }
                    }),
                    "#proxy-failed-resolve-ip");
            }

            return new HealthCheck(GetType());
        }
    }
}

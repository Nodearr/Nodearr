using System.Collections.Generic;
using Microsoft.Extensions.Options;
using NzbDrone.Common.Options;

namespace NzbDrone.Core.Configuration
{
    public interface IEnvironmentFixedSettings
    {
        // The general settings that are given in the environment, by the names the settings page
        // knows them by. Such a setting wins over what is saved, so saving it would change nothing.
        List<string> General();
    }

    // A container is told its port and its key from outside, in the environment. The settings page
    // shows those and does not offer to change them: a port changed here and not in the container's
    // own set-up would leave the page unreachable.
    public class EnvironmentFixedSettings : IEnvironmentFixedSettings
    {
        private readonly ServerOptions _server;
        private readonly AuthOptions _auth;
        private readonly AppOptions _app;
        private readonly LogOptions _log;

        public EnvironmentFixedSettings(IOptions<ServerOptions> server, IOptions<AuthOptions> auth, IOptions<AppOptions> app, IOptions<LogOptions> log)
        {
            _server = server.Value;
            _auth = auth.Value;
            _app = app.Value;
            _log = log.Value;
        }

        public List<string> General()
        {
            var given = new List<string>();

            void Add(string name, object value)
            {
                if (value != null)
                {
                    given.Add(name);
                }
            }

            Add("bindAddress", _server.BindAddress);
            Add("port", _server.Port);
            Add("urlBase", _server.UrlBase);
            Add("enableSsl", _server.EnableSsl);
            Add("sslPort", _server.SslPort);
            Add("sslCertPath", _server.SslCertPath);
            Add("sslKeyPath", _server.SslKeyPath);
            Add("sslCertPassword", _server.SslCertPassword);
            Add("instanceName", _app.InstanceName);
            Add("launchBrowser", _app.LaunchBrowser);
            Add("apiKey", _auth.ApiKey);
            Add("authenticationMethod", _auth.Method);
            Add("authenticationRequired", _auth.Required);
            Add("logLevel", _log.Level);
            Add("logSizeLimit", _log.SizeLimit);

            return given;
        }
    }
}

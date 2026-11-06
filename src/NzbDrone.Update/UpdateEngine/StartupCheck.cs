using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Update.UpdateEngine
{
    public interface IStartupCheck
    {
        // Whether Nodearr answers /ping within the time given.
        bool Answers(TimeSpan within);
    }

    // After an update Nodearr must start and answer its API. If it does not within 3 minutes, the
    // updater puts the old version back (ADR 0024). The address is the one Nodearr serves on, from
    // config.xml or the environment, as Nodearr reads it.
    public class StartupCheck : IStartupCheck
    {
        private static readonly TimeSpan Pause = TimeSpan.FromSeconds(3);

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public StartupCheck(IAppFolderInfo appFolderInfo, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        public bool Answers(TimeSpan within)
        {
            var url = PingUrl();
            var watch = Stopwatch.StartNew();

            _logger.Info("Waiting up to {0:0} seconds for Nodearr to answer at {1}", within.TotalSeconds, url);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            while (watch.Elapsed < within)
            {
                try
                {
                    using var response = client.GetAsync(url).GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.Info("Nodearr answered after {0:0} seconds", watch.Elapsed.TotalSeconds);
                        return true;
                    }

                    _logger.Debug("Nodearr answered {0}", (int)response.StatusCode);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    _logger.Trace("No answer yet: {0}", e.Message);
                }

                Thread.Sleep(Pause);
            }

            return false;
        }

        private string PingUrl()
        {
            var port = Setting("Server", "Port", "Port") ?? "6633";
            var urlBase = (Setting("Server", "UrlBase", "UrlBase") ?? string.Empty).Trim('/');
            var bindAddress = Setting("Server", "BindAddress", "BindAddress") ?? "*";

            var host = bindAddress is "*" or "0.0.0.0" or "::" or "" ? "127.0.0.1" : bindAddress;

            if (host.Contains(':'))
            {
                host = $"[{host}]";
            }

            return urlBase.IsNullOrWhiteSpace() ? $"http://{host}:{port}/ping" : $"http://{host}:{port}/{urlBase}/ping";
        }

        // Nodearr__Server__Port and the like win over config.xml, as in Nodearr itself (ADR 0019).
        private string Setting(string section, string key, string element)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable($"Nodearr__{section}__{key}");

            if (fromEnvironment.IsNotNullOrWhiteSpace())
            {
                return fromEnvironment.Trim();
            }

            try
            {
                var configPath = _appFolderInfo.GetConfigPath();

                if (!File.Exists(configPath))
                {
                    return null;
                }

                var value = XDocument.Load(configPath).Root?.Element(element)?.Value;

                return value.IsNotNullOrWhiteSpace() ? value.Trim() : null;
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Could not read {0} from config.xml", element);
                return null;
            }
        }
    }
}

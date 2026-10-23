using System;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.Nodes.Settings
{
    public interface IRelayProbe
    {
        // Why the relay at this address cannot be used, or null when it answers as a relay.
        string Check(Uri relay);
    }

    // Asks a relay what iroh's own nodes ask one to measure how far away it is: GET /ping at the
    // root of its host, which an iroh relay answers with 200 and nothing else (iroh-relay's
    // RELAY_PROBE_PATH).
    public class RelayProbe : IRelayProbe
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public RelayProbe(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public string Check(Uri relay)
        {
            var ping = new Uri(relay, "/ping");
            var request = new HttpRequest(ping.AbsoluteUri)
            {
                RequestTimeout = Timeout,
                AllowAutoRedirect = false,
                SuppressHttpError = true,
                LogHttpError = false
            };

            try
            {
                var status = (int)_httpClient.Get(request).StatusCode;

                return status is >= 200 and < 300
                    ? null
                    : $"{relay.Authority} answered, but not as a relay: {ping.AbsolutePath} gave HTTP {status}";
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "No relay answered at {0}", relay);

                return $"No relay answered at {relay.Authority}: {ex.Message}";
            }
        }
    }
}

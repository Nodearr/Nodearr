using System;
using System.Net.Http;
using Microsoft.Extensions.Options;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Usage.Commands;

namespace NzbDrone.Core.Usage
{
    public enum UsageReportOutcome
    {
        // Send Anonymous Usage Data, or Contact nodearr.com, is off.
        Off,
        Sent,

        // usage.nodearr.com could not be reached or refused it. Tried again at the next run.
        Failed
    }

    public class UsageReportPreview
    {
        // Whether a report is sent at all, with the settings as they are.
        public bool Sending { get; set; }

        // The report as it would be sent now, exactly.
        public string Report { get; set; }
    }

    public interface IUsageReportService
    {
        // The report the next run would send, whether or not sending is on.
        UsageReportPreview Preview();

        UsageReportOutcome Send();
    }

    // Posts the anonymous usage report to usage.nodearr.com, one request with the report and
    // nothing else (ADR 0024). The JSON sent is written to the debug log. A failure is logged at
    // debug level and not tried again until the next run: it isn't the owner's to fix.
    public class UsageReportService : IUsageReportService, IExecute<SendUsageReportCommand>
    {
        public const string DefaultUrl = "https://usage.nodearr.com/v1/report";

        private readonly IUsageReportBuilder _builder;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IHttpClient _httpClient;
        private readonly UsageOptions _options;
        private readonly Logger _logger;

        public UsageReportService(IUsageReportBuilder builder,
                                  IConfigFileProvider configFileProvider,
                                  IHttpClient httpClient,
                                  IOptions<UsageOptions> options,
                                  Logger logger)
        {
            _builder = builder;
            _configFileProvider = configFileProvider;
            _httpClient = httpClient;
            _options = options.Value ?? new UsageOptions();
            _logger = logger;
        }

        // For testing: the time a report describes.
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public string Url => _options.Url.IsNotNullOrWhiteSpace() ? _options.Url.Trim() : DefaultUrl;

        public bool IsSending => _configFileProvider.ContactNodearr && _configFileProvider.UsageSend;

        public UsageReportPreview Preview()
        {
            return new UsageReportPreview
            {
                Sending = IsSending,
                Report = UsageReportBuilder.ToJson(_builder.Build(UtcNow()))
            };
        }

        public UsageReportOutcome Send()
        {
            if (!IsSending)
            {
                _logger.Debug("Not sending the usage report: {0} is off", _configFileProvider.ContactNodearr ? "Send Anonymous Usage Data" : "Contact nodearr.com");
                return UsageReportOutcome.Off;
            }

            string json;

            try
            {
                json = UsageReportBuilder.ToJson(_builder.Build(UtcNow()));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not put the usage report together. Nothing was sent.");
                return UsageReportOutcome.Failed;
            }

            _logger.Debug("Sending the usage report to {0}:\n{1}", Url, json);

            var request = new HttpRequest(Url)
            {
                Method = HttpMethod.Post,
                UseSimplifiedUserAgent = true,
                AllowAutoRedirect = false,
                StoreRequestCookie = false,
                StoreResponseCookie = false,
                LogHttpError = false,
                RequestTimeout = TimeSpan.FromSeconds(30)
            };

            request.Headers.ContentType = "application/json";
            request.SetContent(json);

            try
            {
                var response = _httpClient.Execute(request);

                if ((int)response.StatusCode >= 300)
                {
                    throw new HttpException(request, response);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not send the usage report to {0}. It is sent again at the next run.", Url);
                return UsageReportOutcome.Failed;
            }

            return UsageReportOutcome.Sent;
        }

        public void Execute(SendUsageReportCommand message)
        {
            Send();
        }
    }
}

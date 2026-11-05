using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Metrics;

namespace Nodearr.Http.Metrics
{
    // What Prometheus scrapes. Always there, with no setting, and it does nothing until it is
    // asked. It takes the API key, as the X-Api-Key header, the apikey query parameter or a
    // bearer token, by the fallback policy (AddAppAuthorization): no attribute here on purpose.
    // Nothing it gives is not already in the API.
    public class MetricsController : Controller
    {
        private readonly IMetricsService _metrics;

        public MetricsController(IMetricsService metrics)
        {
            _metrics = metrics;
        }

        [HttpGet("/metrics")]
        [Produces("text/plain", "application/openmetrics-text")]
        public IActionResult Get()
        {
            var openMetrics = PrometheusText.WantsOpenMetrics(Request.Headers.Accept);
            var text = PrometheusText.Write(_metrics.Collect(), openMetrics);

            return Content(text, openMetrics ? PrometheusText.OpenMetricsContentType : PrometheusText.TextContentType);
        }
    }
}

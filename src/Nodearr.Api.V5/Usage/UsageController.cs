using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Usage;

namespace Nodearr.Api.V5.Usage
{
    [V5ApiController]
    public class UsageController : Controller
    {
        private readonly IUsageReportService _usageReportService;

        public UsageController(IUsageReportService usageReportService)
        {
            _usageReportService = usageReportService;
        }

        // The anonymous usage report as the next run would send it to usage.nodearr.com, exactly,
        // for Show Report on Settings > General. Asking for it sends nothing.
        [HttpGet]
        [Produces("application/json")]
        public Ok<UsageReportResource> GetReport()
        {
            var preview = _usageReportService.Preview();

            return TypedResults.Ok(new UsageReportResource
            {
                Sending = preview.Sending,
                Report = preview.Report
            });
        }
    }

    public class UsageReportResource
    {
        // Whether reports are sent, with Contact nodearr.com and Send Anonymous Usage Data as they are.
        public bool Sending { get; set; }

        // The JSON that would be sent, as text.
        public string Report { get; set; } = string.Empty;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Transfers;

namespace Nodearr.Http.Sabnzbd
{
    // Nodearr as the Arr apps see it when they download: the part of the SABnzbd API that
    // Sonarr's and Radarr's SABnzbd clients call (ADR 0010). A "download" is a file fetched from
    // a trusted node. Anonymous at the framework level so a bad key gets SABnzbd's own error
    // text, which the Arr apps match on.
    [AllowAnonymous]
    public class SabnzbdController : Controller
    {
        // The Arr apps check the version and use newer API fields from 3.x up.
        private const string Version = "4.5.1";

        private readonly ITransferService _transfers;
        private readonly ITransferFolders _folders;
        private readonly IApplicationFactory _applicationFactory;
        private readonly IConfigFileProvider _configFileProvider;

        public SabnzbdController(ITransferService transfers, ITransferFolders folders, IApplicationFactory applicationFactory, IConfigFileProvider configFileProvider)
        {
            _transfers = transfers;
            _folders = folders;
            _applicationFactory = applicationFactory;
            _configFileProvider = configFileProvider;
        }

        [HttpGet("/sabnzbd/api")]
        [HttpPost("/sabnzbd/api")]
        public IActionResult Api([FromQuery] string mode, [FromQuery] string name, [FromQuery] string value, [FromQuery] string apikey)
        {
            if (mode == "version")
            {
                return Json(new JsonObject { ["version"] = Version });
            }

            if (string.IsNullOrWhiteSpace(apikey))
            {
                return Fail("API Key Required");
            }

            if (apikey != _configFileProvider.ApiKey)
            {
                return Fail("API Key Incorrect");
            }

            switch (mode)
            {
                case "get_config":
                    return GetConfig();
                case "fullstatus":
                    return Json(new JsonObject { ["status"] = new JsonObject { ["completedir"] = _folders.Complete } });
                case "addfile":
                    return AddFile();
                case "queue":
                    return name == "delete" ? Delete(value) : Queue();
                case "history":
                    return name == "delete" ? Delete(value) : History();
                case "retry":
                    return Retry(value);
                default:
                    return Fail("not implemented");
            }
        }

        private IActionResult GetConfig()
        {
            var misc = new JsonObject
            {
                ["complete_dir"] = _folders.Complete,
                ["pre_check"] = false,
                ["enable_tv_sorting"] = false,
                ["enable_movie_sorting"] = false,
                ["enable_date_sorting"] = false,
                ["tv_categories"] = new JsonArray(),
                ["movie_categories"] = new JsonArray(),
                ["date_categories"] = new JsonArray(),
                ["history_retention"] = string.Empty,
                ["history_retention_option"] = "all",
                ["history_retention_number"] = 0
            };

            // The Arr apps refuse a download client that does not list the category they use.
            var names = _applicationFactory.All()
                .Select(a => (a.Settings as ArrApplicationSettings)?.DownloadCategory)
                .Concat(new[] { "tv", "movies" })
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Each category's folder exists from the start. The Arr apps check it when they read
            // this, and Sonarr warned of a missing folder until the first transfer for it arrived.
            _folders.EnsureCategoryFolders(names);

            var categories = new JsonArray { Category("*", string.Empty) };

            foreach (var category in names)
            {
                categories.Add(Category(category, category));
            }

            return Json(new JsonObject
            {
                ["config"] = new JsonObject
                {
                    ["misc"] = misc,
                    ["categories"] = categories,
                    ["servers"] = new JsonArray(),
                    ["sorters"] = new JsonArray()
                }
            });
        }

        private static JsonObject Category(string name, string dir)
        {
            return new JsonObject
            {
                ["name"] = name,
                ["order"] = 0,
                ["pp"] = string.Empty,
                ["script"] = "Default",
                ["dir"] = dir,
                ["priority"] = -100
            };
        }

        private IActionResult AddFile()
        {
            var file = Request.HasFormContentType && Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null;

            if (file == null)
            {
                return Fail("expects one parameter");
            }

            string candidateId;
            string airDate;

            try
            {
                using var stream = file.OpenReadStream();

                var ticket = XDocument.Load(stream);

                candidateId = NewznabFeed.ReadTicket(ticket);
                airDate = NewznabFeed.ReadTicketAirDate(ticket);
            }
            catch (Exception)
            {
                return Fail("Invalid NZB");
            }

            if (string.IsNullOrWhiteSpace(candidateId))
            {
                return Fail("This NZB is not a Nodearr ticket. Nodearr can only download what its own indexer returned.");
            }

            var job = _transfers.Add(candidateId, Request.Query["cat"].ToString(), airDate);

            if (job == null)
            {
                return Fail("No trusted node offers this file any more");
            }

            return Json(new JsonObject { ["status"] = true, ["nzo_ids"] = new JsonArray(job.DownloadId) });
        }

        private IActionResult Queue()
        {
            var slots = new JsonArray();
            var index = 0;

            foreach (var job in InCategory(_transfers.All().Where(j => j.IsActive)))
            {
                var left = Math.Max(0, job.Size - job.BytesDone);

                slots.Add(new JsonObject
                {
                    ["status"] = job.State == TransferState.Downloading ? "Downloading" : "Queued",
                    ["index"] = index++,
                    ["timeleft"] = TimeLeft(job, left),
                    ["mb"] = Megabytes(job.Size),
                    ["mbleft"] = Megabytes(left),
                    ["filename"] = job.Title,
                    ["priority"] = "Normal",
                    ["cat"] = job.Category,
                    ["percentage"] = (job.Size == 0 ? 0 : job.BytesDone * 100 / job.Size).ToString(CultureInfo.InvariantCulture),
                    ["nzo_id"] = job.DownloadId
                });
            }

            return Json(new JsonObject { ["queue"] = new JsonObject { ["paused"] = false, ["slots"] = slots } });
        }

        private IActionResult History()
        {
            var slots = new JsonArray();

            foreach (var job in InCategory(_transfers.All().Where(j => !j.IsActive)).OrderByDescending(j => j.Ended))
            {
                var ended = job.Ended ?? DateTime.UtcNow;

                slots.Add(new JsonObject
                {
                    ["nzo_id"] = job.DownloadId,
                    ["name"] = job.Title,
                    ["nzb_name"] = job.Title + ".nzb",
                    ["category"] = job.Category,
                    ["bytes"] = job.Size,
                    ["status"] = job.State == TransferState.Completed ? "Completed" : "Failed",
                    ["fail_message"] = job.State == TransferState.Failed ? job.FailureMessage ?? string.Empty : string.Empty,
                    ["storage"] = job.OutputPath ?? string.Empty,
                    ["download_time"] = (int)(ended - (job.Started ?? job.Added)).TotalSeconds,
                    ["completed"] = new DateTimeOffset(DateTime.SpecifyKind(ended, DateTimeKind.Utc)).ToUnixTimeSeconds()
                });
            }

            return Json(new JsonObject { ["history"] = new JsonObject { ["slots"] = slots } });
        }

        private IEnumerable<TransferJob> InCategory(IEnumerable<TransferJob> jobs)
        {
            var category = Request.Query["category"].ToString();

            return string.IsNullOrWhiteSpace(category) ? jobs : jobs.Where(j => string.Equals(j.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        private IActionResult Delete(string downloadId)
        {
            var removed = _transfers.Remove(downloadId, Request.Query["del_files"] == "1");

            return Json(new JsonObject { ["status"] = removed, ["nzo_ids"] = new JsonArray(downloadId) });
        }

        private IActionResult Retry(string downloadId)
        {
            var job = _transfers.Retry(downloadId);

            if (job == null)
            {
                return Fail("No such failed job");
            }

            return Json(new JsonObject { ["status"] = true, ["nzo_id"] = job.DownloadId });
        }

        // How long is left at the speed so far, the way SABnzbd writes it.
        private static string TimeLeft(TransferJob job, long bytesLeft)
        {
            var elapsed = job.Started.HasValue ? (DateTime.UtcNow - job.Started.Value).TotalSeconds : 0;

            if (job.BytesDone <= 0 || elapsed <= 0)
            {
                return "0:00:00";
            }

            var seconds = Math.Min(bytesLeft / (job.BytesDone / elapsed), 99 * 3600);
            var left = TimeSpan.FromSeconds(seconds);

            return $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}";
        }

        private static string Megabytes(long bytes)
        {
            return (bytes / 1024m / 1024m).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private IActionResult Fail(string error)
        {
            return Json(new JsonObject { ["status"] = false, ["error"] = error });
        }

        private IActionResult Json(JsonObject body)
        {
            return Content(body.ToJsonString(), "application/json", Encoding.UTF8);
        }
    }
}

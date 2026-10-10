using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;

namespace Nodearr.Http.ArrContractSpike
{
    // S1 spike: the part of the SABnzbd API that Sonarr's and Radarr's SABnzbd clients call.
    // Anonymous at the framework level so a bad key gets SABnzbd's own error text, which Arr matches on.
    [AllowAnonymous]
    public class SabnzbdSpikeController : Controller
    {
        private const string Version = "4.5.1";

        private readonly IArrContractSpikeStore _store;
        private readonly IConfigFileProvider _configFileProvider;

        public SabnzbdSpikeController(IArrContractSpikeStore store, IConfigFileProvider configFileProvider)
        {
            _store = store;
            _configFileProvider = configFileProvider;
        }

        [HttpGet("/sabnzbd/api")]
        [HttpPost("/sabnzbd/api")]
        public IActionResult Api([FromQuery] string mode, [FromQuery] string name, [FromQuery] string value, [FromQuery] string apikey)
        {
            if (mode == "version")
            {
                return Json(new JsonObject { ["version"] = Version }, "version " + Version);
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
                    return Json(new JsonObject { ["status"] = new JsonObject { ["completedir"] = _store.GetConfig().CompleteDirReported } }, "fullstatus");
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
            var config = _store.GetConfig();

            var misc = new JsonObject
            {
                ["complete_dir"] = config.CompleteDirReported,
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

            var categories = new JsonArray(
                Category("*", string.Empty),
                Category("tv", "tv"),
                Category("movies", "movies"));

            return Json(new JsonObject
            {
                ["config"] = new JsonObject
                {
                    ["misc"] = misc,
                    ["categories"] = categories,
                    ["servers"] = new JsonArray(),
                    ["sorters"] = new JsonArray()
                }
            },
            "config: complete_dir " + config.CompleteDirReported + ", categories *, tv, movies");
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

            try
            {
                using var stream = file.OpenReadStream();
                var nzb = XDocument.Load(stream);

                candidateId = nzb.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "meta" && (string)e.Attribute("type") == "x-nodearr-candidate")?.Value;
            }
            catch (Exception)
            {
                return Fail("Invalid NZB");
            }

            var candidate = _store.GetConfig().Candidates.SingleOrDefault(c => c.Id == candidateId);

            if (candidate == null)
            {
                return Fail("NZB is not a Nodearr ticket for a known candidate");
            }

            var job = _store.AddJob(candidate, Request.Query["cat"].ToString(), Path.GetFileName(file.FileName));

            return Json(new JsonObject { ["status"] = true, ["nzo_ids"] = new JsonArray(job.NzoId) },
                $"accepted {file.FileName} ({file.Length} bytes, field '{file.Name}') for candidate {candidate.Id} as {job.NzoId}");
        }

        private IActionResult Queue()
        {
            var category = Request.Query["category"].ToString();
            var jobs = _store.GetJobs()
                .Where(j => j.Status == SpikeJobStatus.Queued || j.Status == SpikeJobStatus.Downloading)
                .Where(j => string.IsNullOrWhiteSpace(category) || j.Category == category)
                .ToList();

            var slots = new JsonArray();
            var index = 0;

            foreach (var job in jobs)
            {
                var left = Math.Max(0, job.TotalBytes - job.DoneBytes);

                slots.Add(new JsonObject
                {
                    ["status"] = job.Status.ToString(),
                    ["index"] = index++,
                    ["timeleft"] = "0:00:05",
                    ["mb"] = Megabytes(job.TotalBytes),
                    ["mbleft"] = Megabytes(left),
                    ["filename"] = job.Candidate.Title,
                    ["priority"] = "Normal",
                    ["cat"] = job.Category,
                    ["percentage"] = (job.TotalBytes == 0 ? 0 : job.DoneBytes * 100 / job.TotalBytes).ToString(),
                    ["nzo_id"] = job.NzoId
                });
            }

            return Json(new JsonObject { ["queue"] = new JsonObject { ["paused"] = false, ["slots"] = slots } },
                $"queue: {string.Join(", ", jobs.Select(j => $"{j.NzoId} {j.Status} {(j.TotalBytes == 0 ? 0 : j.DoneBytes * 100 / j.TotalBytes)}%"))}");
        }

        private IActionResult History()
        {
            var category = Request.Query["category"].ToString();
            var jobs = _store.GetJobs()
                .Where(j => j.Status == SpikeJobStatus.Completed || j.Status == SpikeJobStatus.Failed)
                .Where(j => string.IsNullOrWhiteSpace(category) || j.Category == category)
                .OrderByDescending(j => j.CompletedAt)
                .ToList();

            var slots = new JsonArray();

            foreach (var job in jobs)
            {
                slots.Add(new JsonObject
                {
                    ["nzo_id"] = job.NzoId,
                    ["name"] = job.Candidate.Title,
                    ["nzb_name"] = job.NzbName,
                    ["category"] = job.Category,
                    ["bytes"] = job.TotalBytes,
                    ["status"] = job.Status.ToString(),
                    ["fail_message"] = job.FailMessage ?? string.Empty,
                    ["storage"] = job.StorageReported ?? string.Empty,
                    ["download_time"] = (int)((job.CompletedAt ?? DateTime.UtcNow) - job.AddedAt).TotalSeconds,
                    ["completed"] = new DateTimeOffset(job.CompletedAt ?? DateTime.UtcNow).ToUnixTimeSeconds()
                });
            }

            return Json(new JsonObject { ["history"] = new JsonObject { ["slots"] = slots } },
                $"history: {string.Join(", ", jobs.Select(j => $"{j.NzoId} {j.Status} {j.StorageReported}{j.FailMessage}"))}");
        }

        private IActionResult Delete(string nzoId)
        {
            var removed = _store.Remove(nzoId, Request.Query["del_files"] == "1");

            return Json(new JsonObject { ["status"] = removed, ["nzo_ids"] = new JsonArray(nzoId) }, $"delete {nzoId}: {removed}");
        }

        private IActionResult Retry(string nzoId)
        {
            var job = _store.Retry(nzoId);

            if (job == null)
            {
                return Fail("No such failed job");
            }

            return Json(new JsonObject { ["status"] = true, ["nzo_id"] = job.NzoId }, $"retry {nzoId} as {job.NzoId}");
        }

        private static string Megabytes(long bytes)
        {
            return (bytes / 1024m / 1024m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        private IActionResult Fail(string error)
        {
            return Json(new JsonObject { ["status"] = false, ["error"] = error }, "error: " + error);
        }

        private IActionResult Json(JsonObject body, string summary)
        {
            _store.Record(Request, 200, summary);

            return Content(body.ToJsonString(), "application/json", Encoding.UTF8);
        }
    }
}

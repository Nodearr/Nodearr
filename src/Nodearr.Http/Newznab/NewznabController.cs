using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.PeerIndexer;

namespace Nodearr.Http.Newznab
{
    // Nodearr as the Arr apps see it when they search: a Newznab indexer over what trusted nodes
    // share. Anonymous at the framework level so a bad key gets a Newznab error document, which
    // the Arr apps understand, instead of a bare 401.
    [AllowAnonymous]
    public class NewznabController : Controller
    {
        private readonly ICandidateService _candidates;
        private readonly IConfigFileProvider _configFileProvider;

        public NewznabController(ICandidateService candidates, IConfigFileProvider configFileProvider)
        {
            _candidates = candidates;
            _configFileProvider = configFileProvider;
        }

        [HttpGet("/newznab/api")]
        public IActionResult Api([FromQuery] string t, [FromQuery] string apikey)
        {
            var function = (t ?? string.Empty).ToLowerInvariant();

            if (function == "caps")
            {
                return Xml(NewznabFeed.Caps(BaseUrl()));
            }

            if (apikey != _configFileProvider.ApiKey)
            {
                return Xml(NewznabFeed.Error(100, "Incorrect user credentials"));
            }

            switch (function)
            {
                case "search":
                case "tvsearch":
                case "movie":
                    return Search(function);
                case "get":
                    return Get();
                default:
                    return Xml(NewznabFeed.Error(202, "No such function"));
            }
        }

        private IActionResult Search(string function)
        {
            var categories = Request.Query["cat"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => int.TryParse(c, out var id) ? id : 0)
                .Where(c => c > 0)
                .ToList();

            // Nodearr has two kinds of thing, TV and movies. Any category in a family asks for
            // that kind, and how good a file is stays for the Arr app to judge from its title.
            bool Asked(int family) => categories.Count == 0 || categories.Any(c => c / 1000 * 1000 == family);

            var query = new CandidateQuery
            {
                IncludeTv = function != "movie" && Asked(NewznabFeed.TvCategory),
                IncludeMovies = function != "tvsearch" && Asked(NewznabFeed.MovieCategory),
                TvdbId = Number("tvdbid"),
                TmdbId = Number("tmdbid"),
                ImdbId = Request.Query["imdbid"].ToString(),
                HasUnknownIds = new[] { "rid", "tvmazeid", "traktid", "doubanid" }.Any(id => !string.IsNullOrWhiteSpace(Request.Query[id])),
                Season = int.TryParse(Request.Query["season"].ToString().TrimStart('S', 's'), out var season) ? season : null,
                Episode = Request.Query["ep"].ToString().TrimStart('E', 'e'),
                Text = Request.Query["q"].ToString(),
                Offset = Number("offset"),
                Limit = Request.Query.ContainsKey("limit") ? Number("limit") : CandidateService.MaxLimit
            };

            var result = _candidates.Search(query);
            var items = result.Items.Select(i => NewznabFeed.Item(i, Link(i.CandidateId))).ToList();
            var total = result.Total;

            if (total == 0 && query.IsBrowse)
            {
                items = NewznabFeed.ConnectionTestItems(query.IncludeTv, query.IncludeMovies, categories, BaseUrl()).ToList();
                total = items.Count;
            }

            return Xml(NewznabFeed.Rss(BaseUrl(), Math.Max(0, query.Offset), total, items));
        }

        private IActionResult Get()
        {
            var item = _candidates.Find(Request.Query["id"].ToString());

            if (item == null)
            {
                return Xml(NewznabFeed.Error(300, "No such item"));
            }

            return File(Document(NewznabFeed.Ticket(item)), "application/x-nzb", item.ReleaseTitle + ".nzb");
        }

        private int Number(string name)
        {
            return int.TryParse(Request.Query[name], out var value) ? value : 0;
        }

        private string Link(string candidateId)
        {
            return $"{BaseUrl()}/newznab/api?t=get&id={Uri.EscapeDataString(candidateId)}&apikey={_configFileProvider.ApiKey}";
        }

        // The address the Arr app used to get here, so the link it is handed is one it can reach.
        private string BaseUrl()
        {
            return $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        }

        private IActionResult Xml(XElement root)
        {
            return File(Document(root), "application/xml; charset=utf-8");
        }

        private static byte[] Document(XElement root)
        {
            return Encoding.UTF8.GetBytes(new XDeclaration("1.0", "utf-8", null) + Environment.NewLine + root);
        }
    }
}

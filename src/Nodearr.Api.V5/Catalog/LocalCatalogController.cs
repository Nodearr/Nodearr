using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.Extensions;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Datastore;

namespace Nodearr.Api.V5.Catalog;

[V5ApiController("catalog/local")]
public class LocalCatalogController : Controller
{
    private static readonly HashSet<string> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "path",
        "size",
        "quality",
        "dateAdded",
        "missingSince"
    };

    private readonly ICatalogService _catalogService;
    private readonly IApplicationFactory _applicationFactory;
    private readonly ICatalogFileLocator _locator;

    public LocalCatalogController(ICatalogService catalogService,
                                  IApplicationFactory applicationFactory,
                                  ICatalogFileLocator locator)
    {
        _catalogService = catalogService;
        _applicationFactory = applicationFactory;
        _locator = locator;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<LocalCatalogSummaryResource>> GetSummary()
    {
        return TypedResults.Ok(_catalogService.GetSummaries().ConvertAll(LocalCatalogSummaryResource.FromModel));
    }

    // One entry for each series or movie with a file, by sort title, for the Posters view.
    [HttpGet("titles")]
    [Produces("application/json")]
    public Ok<PagingResource<LocalCatalogTitleResource>> GetTitles([FromQuery] PagingRequestResource paging, [FromQuery] int? applicationId = null)
    {
        var page = Math.Max(paging.Page ?? 1, 1);
        var pageSize = Math.Clamp(paging.PageSize ?? 50, 1, 250);
        var titles = _catalogService.GetTitles(applicationId);
        var applications = GetApplications();

        return TypedResults.Ok(new PagingResource<LocalCatalogTitleResource>
        {
            Page = page,
            PageSize = pageSize,
            SortKey = "title",
            SortDirection = SortDirection.Ascending,
            TotalRecords = titles.Count,
            Records = titles.Skip((page - 1) * pageSize).Take(pageSize).Select(t => new LocalCatalogTitleResource
            {
                ApplicationId = t.ApplicationId,
                ApplicationName = applications.TryGetValue(t.ApplicationId, out var application) ? application.Name : null,
                MediaType = t.Subject == PosterSubject.Series ? "series" : "movie",
                PosterSubject = t.Subject,
                PosterArrId = t.ArrId,
                Title = t.Title,
                Year = t.Year,
                Files = t.Files
            }).ToList()
        });
    }

    [HttpGet("episodefile")]
    [Produces("application/json")]
    public Ok<PagingResource<LocalCatalogFileResource>> GetEpisodeFiles([FromQuery] PagingRequestResource paging, [FromQuery] int? applicationId = null)
    {
        var pagingSpec = new PagingResource<LocalCatalogFileResource>(paging)
            .MapToPagingSpec<LocalCatalogFileResource, CatalogEpisodeFile>(SortKeys, "path", SortDirection.Ascending);

        if (applicationId.HasValue)
        {
            pagingSpec.FilterExpressions.Add(f => f.ApplicationId == applicationId.Value);
        }

        var applications = GetApplications();
        var series = new Dictionary<(int, int), CatalogSeries>();
        var episodes = new Dictionary<(int, int), List<CatalogEpisode>>();

        return TypedResults.Ok(pagingSpec.ApplyToPage(
            spec =>
            {
                var page = _catalogService.PagedEpisodeFiles(spec);

                foreach (var group in page.Records.GroupBy(f => f.ApplicationId))
                {
                    foreach (var item in _catalogService.GetSeries(group.Key, group.Select(f => f.ArrSeriesId)))
                    {
                        series[(group.Key, item.ArrId)] = item;
                    }

                    foreach (var byFile in _catalogService.GetEpisodesForFiles(group.Key, group.Select(f => f.ArrId)).GroupBy(e => e.ArrEpisodeFileId))
                    {
                        episodes[(group.Key, byFile.Key)] = byFile.OrderBy(e => e.EpisodeNumber).ToList();
                    }
                }

                return page;
            },
            file =>
            {
                var resource = LocalCatalogFileResource.FromFile(file, "episode");

                resource.PosterSubject = PosterSubject.Series;
                resource.PosterArrId = file.ArrSeriesId;
                resource.SeasonNumber = file.SeasonNumber;

                if (series.TryGetValue((file.ApplicationId, file.ArrSeriesId), out var parent))
                {
                    resource.Title = parent.Title;
                    resource.Year = parent.Year;
                    resource.TvdbId = parent.TvdbId;
                    resource.TmdbId = parent.TmdbId;
                    resource.ImdbId = parent.ImdbId;
                }

                if (episodes.TryGetValue((file.ApplicationId, file.ArrId), out var contained))
                {
                    resource.EpisodeNumbers = contained.Select(e => e.EpisodeNumber).ToList();
                    resource.EpisodeTitles = contained.Select(e => e.Title ?? string.Empty).ToList();
                }

                return Locate(resource, applications);
            }));
    }

    [HttpGet("moviefile")]
    [Produces("application/json")]
    public Ok<PagingResource<LocalCatalogFileResource>> GetMovieFiles([FromQuery] PagingRequestResource paging, [FromQuery] int? applicationId = null)
    {
        var pagingSpec = new PagingResource<LocalCatalogFileResource>(paging)
            .MapToPagingSpec<LocalCatalogFileResource, CatalogMovieFile>(SortKeys, "path", SortDirection.Ascending);

        if (applicationId.HasValue)
        {
            pagingSpec.FilterExpressions.Add(f => f.ApplicationId == applicationId.Value);
        }

        var applications = GetApplications();
        var movies = new Dictionary<(int, int), CatalogMovie>();

        return TypedResults.Ok(pagingSpec.ApplyToPage(
            spec =>
            {
                var page = _catalogService.PagedMovieFiles(spec);

                foreach (var group in page.Records.GroupBy(f => f.ApplicationId))
                {
                    foreach (var item in _catalogService.GetMovies(group.Key, group.Select(f => f.ArrMovieId)))
                    {
                        movies[(group.Key, item.ArrId)] = item;
                    }
                }

                return page;
            },
            file =>
            {
                var resource = LocalCatalogFileResource.FromFile(file, "movie");

                resource.PosterSubject = PosterSubject.Movie;
                resource.PosterArrId = file.ArrMovieId;

                if (movies.TryGetValue((file.ApplicationId, file.ArrMovieId), out var parent))
                {
                    resource.Title = parent.Title;
                    resource.Year = parent.Year;
                    resource.TmdbId = parent.TmdbId;
                    resource.ImdbId = parent.ImdbId;
                }

                return Locate(resource, applications);
            }));
    }

    private Dictionary<int, (string Name, string Host)> GetApplications()
    {
        return _applicationFactory.All().ToDictionary(
            a => a.Id,
            a => (a.Name, Uri.TryCreate((a.Settings as ArrApplicationSettings)?.BaseUrl, UriKind.Absolute, out var url) ? url.Host : string.Empty));
    }

    // Path mappings are keyed by the Arr app's host, the same way Arr keys them by download client host.
    private LocalCatalogFileResource Locate(LocalCatalogFileResource resource, Dictionary<int, (string Name, string Host)> applications)
    {
        if (!applications.TryGetValue(resource.ApplicationId, out var application) || string.IsNullOrWhiteSpace(resource.Path))
        {
            return resource;
        }

        resource.ApplicationName = application.Name;

        var located = _locator.Locate(resource.ApplicationId, resource.Path);

        resource.LocalPath = located.LocalPath;
        resource.Access = _locator.Open(located);
        resource.Readable = resource.Access == CatalogFileAccess.Readable;

        return resource;
    }
}

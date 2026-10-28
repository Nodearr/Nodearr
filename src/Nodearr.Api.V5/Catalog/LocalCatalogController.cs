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
        "missingSince",
        "trackNumber"
    };

    private static readonly HashSet<string> SeriesSortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "title",
        "sortTitle",
        "year"
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

    [HttpGet("details/{kind}/{applicationId:int}/{arrId:int}")]
    [Produces("application/json")]
    public Results<Ok<CatalogDetailsResource>, NotFound> GetDetails(string kind, int applicationId, int arrId)
    {
        if (kind == "series")
        {
            var series = _catalogService.GetSeries(applicationId, new[] { arrId }).FirstOrDefault();
            if (series == null)
            {
                return TypedResults.NotFound();
            }

            var episodeFiles = _catalogService.GetEpisodeFilesForSeries(applicationId, new[] { arrId });
            var availableFiles = episodeFiles.Where(f => !f.MissingSince.HasValue).Select(f => f.ArrId).ToHashSet();
            return TypedResults.Ok(new CatalogDetailsResource
            {
                Kind = kind,
                Title = series.Title,
                SourceName = GetApplications().GetValueOrDefault(applicationId).Name,
                PosterPath = $"/catalog/posters/local/{applicationId}/series/{series.ArrId}",
                Year = series.Year,
                Overview = series.Overview,
                Genres = series.Genres,
                Network = series.Network,
                Runtime = series.Runtime,
                Resolutions = episodeFiles
                    .Where(f => !f.MissingSince.HasValue && f.Resolution > 0)
                    .Select(f => f.Resolution).Distinct().OrderByDescending(r => r).ToList(),
                Episodes = _catalogService.GetEpisodesForSeries(applicationId, new[] { arrId })
                    .OrderBy(e => e.SeasonNumber == 0 ? int.MaxValue : e.SeasonNumber)
                    .ThenBy(e => e.EpisodeNumber)
                    .Select(e => new CatalogDetailsEpisodeResource
                    {
                        SeasonNumber = e.SeasonNumber,
                        EpisodeNumber = e.EpisodeNumber,
                        Title = e.Title,
                        HasFile = !e.MissingSince.HasValue && e.ArrEpisodeFileId != 0 && availableFiles.Contains(e.ArrEpisodeFileId),
                        Path = $"/catalog/local/details/episode/{applicationId}/{e.ArrId}"
                    }).ToList(),
                Send = new CatalogDetailsSendResource { MediaKind = "tv", ApplicationId = applicationId, ArrId = series.ArrId },
                TvdbId = series.TvdbId,
                TmdbId = series.TmdbId,
                ImdbId = series.ImdbId
            });
        }

        if (kind == "episode")
        {
            var episode = _catalogService.GetEpisodes(applicationId, new[] { arrId }).FirstOrDefault();
            if (episode == null)
            {
                return TypedResults.NotFound();
            }

            var series = _catalogService.GetSeries(applicationId, new[] { episode.ArrSeriesId }).FirstOrDefault();
            if (series == null)
            {
                return TypedResults.NotFound();
            }

            var episodeFile = _catalogService.GetEpisodeFilesForSeries(applicationId, new[] { episode.ArrSeriesId })
                .FirstOrDefault(f => f.ArrId == episode.ArrEpisodeFileId && !f.MissingSince.HasValue);

            return TypedResults.Ok(new CatalogDetailsResource
            {
                Kind = kind,
                Title = episode.Title,
                ParentTitle = series.Title,
                SourceName = GetApplications().GetValueOrDefault(applicationId).Name,
                PosterPath = $"/catalog/posters/local/{applicationId}/series/{series.ArrId}",
                Year = series.Year,
                Overview = episode.Overview,
                Genres = series.Genres,
                Network = series.Network,
                Runtime = series.Runtime,
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber,
                AirDate = episode.AirDate,
                Quality = episodeFile?.Quality,
                Resolution = episodeFile?.Resolution ?? 0,
                Size = episodeFile?.Size ?? 0,
                Send = episodeFile != null && !episode.MissingSince.HasValue && _locator.Open(_locator.Locate(applicationId, episodeFile.Path)) == CatalogFileAccess.Readable
                    ? new CatalogDetailsSendResource { MediaKind = "tv", ApplicationId = applicationId, ArrId = series.ArrId, EpisodeArrId = episode.ArrId }
                    : null,
                TvdbId = series.TvdbId,
                TmdbId = series.TmdbId,
                ImdbId = series.ImdbId
            });
        }

        if (kind is "movie" or "film" or "scene")
        {
            var movie = _catalogService.GetMovies(applicationId, new[] { arrId }).FirstOrDefault();
            if (movie == null ||
                kind != (movie.ItemType == "scene" ? "scene" : movie.IsAdult ? "film" : "movie"))
            {
                return TypedResults.NotFound();
            }

            var movieFile = _catalogService.GetMovieFiles(applicationId, new[] { movie.ArrMovieFileId })
                .FirstOrDefault(f => !f.MissingSince.HasValue);

            return TypedResults.Ok(new CatalogDetailsResource
            {
                Kind = kind,
                Title = movie.Title,
                SourceName = GetApplications().GetValueOrDefault(applicationId).Name,
                PosterPath = $"/catalog/posters/local/{applicationId}/movie/{movie.ArrId}",
                Year = movie.Year,
                Overview = movie.Overview,
                Genres = movie.Genres,
                Runtime = movie.Runtime,
                Studio = movie.Studio,
                ReleaseDate = movie.ReleaseDate,
                Code = movie.Code,
                TmdbId = movie.TmdbId,
                ImdbId = movie.ImdbId,
                Quality = movieFile?.Quality,
                Resolution = movieFile?.Resolution ?? 0,
                Size = movieFile?.Size ?? 0,
                Send = movieFile != null && _locator.Open(_locator.Locate(applicationId, movieFile.Path)) == CatalogFileAccess.Readable
                    ? new CatalogDetailsSendResource { MediaKind = "movie", ApplicationId = applicationId, ArrId = movie.ArrId }
                    : null
            });
        }

        return TypedResults.NotFound();
    }

    // One entry for each series or movie with a file, by sort title, for the Posters view.
    [HttpGet("titles")]
    [Produces("application/json")]
    public Ok<PagingResource<LocalCatalogTitleResource>> GetTitles([FromQuery] PagingRequestResource paging, [FromQuery] int? applicationId = null, [FromQuery] string? kind = null)
    {
        var page = Math.Max(paging.Page ?? 1, 1);
        var pageSize = Math.Clamp(paging.PageSize ?? 50, 1, 250);
        var titles = _catalogService.GetTitles(applicationId)
            .Where(t => string.IsNullOrWhiteSpace(kind) || string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .ToList();
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
                MediaType = t.Subject switch
                {
                    PosterSubject.Series => "series",
                    PosterSubject.Album => "album",
                    _ => "movie"
                },
                Kind = t.Kind,
                IsAdult = t.IsAdult,
                PosterSubject = t.Subject,
                PosterArrId = t.ArrId,
                Title = t.Title,
                Year = t.Year,
                Files = t.Files,
                Artist = t.Artist,
                ArtistArrId = t.ArtistArrId
            }).ToList()
        });
    }

    [HttpGet("series")]
    [Produces("application/json")]
    public Ok<PagingResource<LocalCatalogSeriesResource>> GetSeries([FromQuery] PagingRequestResource paging, [FromQuery] int? applicationId = null, [FromQuery] bool onlyWithFiles = false)
    {
        var pagingSpec = new PagingResource<LocalCatalogSeriesResource>(paging)
            .MapToPagingSpec<LocalCatalogSeriesResource, CatalogSeries>(SeriesSortKeys, "sortTitle", SortDirection.Ascending);

        if (applicationId.HasValue)
        {
            pagingSpec.FilterExpressions.Add(s => s.ApplicationId == applicationId.Value);
        }

        var applications = GetApplications();
        var episodes = new Dictionary<(int ApplicationId, int SeriesId), List<CatalogEpisode>>();
        var files = new Dictionary<(int ApplicationId, int SeriesId), List<CatalogEpisodeFile>>();

        return TypedResults.Ok(pagingSpec.ApplyToPage(
            spec =>
            {
                var page = _catalogService.PagedSeries(spec, onlyWithFiles);

                foreach (var group in page.Records.GroupBy(s => s.ApplicationId))
                {
                    var seriesIds = group.Select(s => s.ArrId).ToList();
                    foreach (var episodeGroup in _catalogService.GetEpisodesForSeries(group.Key, seriesIds).GroupBy(e => e.ArrSeriesId))
                    {
                        episodes[(group.Key, episodeGroup.Key)] = episodeGroup.ToList();
                    }

                    foreach (var fileGroup in _catalogService.GetEpisodeFilesForSeries(group.Key, seriesIds).GroupBy(f => f.ArrSeriesId))
                    {
                        files[(group.Key, fileGroup.Key)] = fileGroup.ToList();
                    }
                }

                return page;
            },
            series =>
            {
                var key = (series.ApplicationId, series.ArrId);
                var seriesEpisodes = episodes.GetValueOrDefault(key) ?? new List<CatalogEpisode>();
                var seriesFiles = files.GetValueOrDefault(key) ?? new List<CatalogEpisodeFile>();
                var seasons = CatalogEpisodeGrouping.Build(series.SeriesType, seriesEpisodes, seriesFiles)
                    .Select(season => new { Season = season, Rows = onlyWithFiles ? season.Files.Where(row => !row.IsMissing).ToList() : season.Files })
                    .Where(season => season.Rows.Count > 0)
                    .Select(season => new LocalCatalogSeasonResource
                    {
                        SeasonNumber = season.Season.SeasonNumber,
                        Episodes = season.Rows.Sum(row => row.Episodes.Count),
                        Missing = season.Rows.Where(row => row.IsMissing).Sum(row => row.Episodes.Count),
                        Size = season.Rows.Where(row => row.File != null).Sum(row => row.File.Size),
                        Files = season.Rows.Select(row => BuildEpisodeFileResource(series, row, applications)).ToList()
                    }).ToList();

                return new LocalCatalogSeriesResource
                {
                    ApplicationId = series.ApplicationId,
                    ApplicationName = applications.TryGetValue(series.ApplicationId, out var application) ? application.Name : null,
                    Title = series.Title,
                    Year = series.Year,
                    SeriesType = series.SeriesType,
                    PosterArrId = series.ArrId,
                    Episodes = seasons.Sum(s => s.Episodes),
                    Missing = seasons.Sum(s => s.Missing),
                    Size = seasons.Sum(s => s.Size),
                    Seasons = seasons
                };
            }));
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
                    resource.EpisodeArrIds = contained.Select(e => e.ArrId).ToList();
                    resource.EpisodeTitles = contained.Select(e => e.Title ?? string.Empty).ToList();
                }

                return Locate(resource, applications);
            }));
    }

    [HttpGet("moviefile")]
    [Produces("application/json")]
    public Ok<PagingResource<LocalCatalogFileResource>> GetMovieFiles([FromQuery] PagingRequestResource paging, [FromQuery] int? applicationId = null, [FromQuery] string? kind = null)
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
                var page = _catalogService.PagedMovieFiles(spec, kind);

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
                    resource.IsAdult = parent.IsAdult;
                    resource.IsScene = parent.ItemType == "scene";
                }

                return Locate(resource, applications);
            }));
    }

    [HttpGet("trackfile")]
    [Produces("application/json")]
    public Ok<PagingResource<LocalCatalogFileResource>> GetTrackFiles([FromQuery] PagingRequestResource paging, [FromQuery] int? applicationId = null)
    {
        var pagingSpec = new PagingResource<LocalCatalogFileResource>(paging)
            .MapToPagingSpec<LocalCatalogFileResource, CatalogTrackFile>(SortKeys, "path", SortDirection.Ascending);

        if (applicationId.HasValue)
        {
            pagingSpec.FilterExpressions.Add(f => f.ApplicationId == applicationId.Value);
        }

        var applications = GetApplications();
        var albums = new Dictionary<(int, int), CatalogAlbum>();
        var artists = new Dictionary<(int, int), CatalogArtist>();

        return TypedResults.Ok(pagingSpec.ApplyToPage(
            spec =>
            {
                var page = _catalogService.PagedTrackFiles(spec);

                foreach (var group in page.Records.GroupBy(f => f.ApplicationId))
                {
                    foreach (var album in _catalogService.GetAlbums(group.Key, group.Select(f => f.ArrAlbumId)))
                    {
                        albums[(group.Key, album.ArrId)] = album;
                    }

                    foreach (var artist in _catalogService.GetArtists(group.Key, group.Select(f => f.ArrArtistId)))
                    {
                        artists[(group.Key, artist.ArrId)] = artist;
                    }
                }

                return page;
            },
            file =>
            {
                var resource = LocalCatalogFileResource.FromFile(file, "track");
                resource.PosterSubject = PosterSubject.Album;
                resource.PosterArrId = file.ArrAlbumId;
                resource.ArtistArrId = file.ArrArtistId;
                resource.TrackTitle = file.TrackTitle;
                resource.TrackNumber = file.TrackNumber;
                resource.DiscNumber = file.DiscNumber;
                resource.IsMissing = file.MissingSince.HasValue;

                if (albums.TryGetValue((file.ApplicationId, file.ArrAlbumId), out var album))
                {
                    resource.Title = album.Title;
                    resource.AlbumTitle = album.Title;
                    resource.Year = album.Year;
                }

                if (artists.TryGetValue((file.ApplicationId, file.ArrArtistId), out var artist))
                {
                    resource.Artist = artist.Name;
                }

                return Locate(resource, applications);
            }));
    }

    private LocalCatalogFileResource BuildEpisodeFileResource(CatalogSeries series, CatalogEpisodeFileGroup row, Dictionary<int, (string Name, string Host)> applications)
    {
        var episodes = row.Episodes;
        var resource = row.File != null
            ? LocalCatalogFileResource.FromFile(row.File, "episode")
            : new LocalCatalogFileResource
            {
                Id = episodes.FirstOrDefault()?.Id * -1 ?? 0,
                MediaType = "episode",
                ApplicationId = series.ApplicationId,
                MissingSince = episodes.Select(e => e.MissingSince).FirstOrDefault(d => d.HasValue)
            };

        resource.PosterSubject = PosterSubject.Series;
        resource.PosterArrId = series.ArrId;
        resource.Title = series.Title;
        resource.Year = series.Year;
        resource.TvdbId = series.TvdbId;
        resource.TmdbId = series.TmdbId;
        resource.ImdbId = series.ImdbId;
        resource.SeasonNumber = row.SeasonNumber;
        resource.SeriesType = series.SeriesType;
        resource.EpisodeNumbers = episodes.Select(e => e.EpisodeNumber).ToList();
        resource.EpisodeArrIds = episodes.Select(e => e.ArrId).ToList();
        resource.EpisodeTitles = episodes.Select(e => e.Title ?? string.Empty).ToList();
        resource.AbsoluteEpisodeNumbers = episodes.Where(e => e.AbsoluteEpisodeNumber.HasValue).Select(e => e.AbsoluteEpisodeNumber.GetValueOrDefault()).ToList();
        resource.SceneSeasonNumber = episodes.Select(e => e.SceneSeasonNumber).Distinct().Count() == 1 ? episodes.FirstOrDefault()?.SceneSeasonNumber : null;
        resource.SceneEpisodeNumbers = episodes.Select(e => e.SceneEpisodeNumber ?? e.EpisodeNumber).ToList();
        resource.AirDates = episodes.Select(e => e.AirDate).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        resource.IsMissing = row.IsMissing;

        if (row.File == null)
        {
            resource.Access = CatalogFileAccess.Missing;
            resource.Readable = false;
        }

        return Locate(resource, applications);
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

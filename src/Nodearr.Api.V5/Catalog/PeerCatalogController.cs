using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.Extensions;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace Nodearr.Api.V5.Catalog;

public class PeerCatalogSummaryResource
{
    public int PeerId { get; set; }
    public string? PeerName { get; set; }
    public string? CatalogVersion { get; set; }
    public DateTime? CatalogSyncedAt { get; set; }
    public string? CatalogError { get; set; }
    public int NotInLibrary { get; set; }
    public int Missing { get; set; }
    public int Upgrade { get; set; }
    public int Have { get; set; }
}

public class PeerCatalogItemResource
{
    public int Id { get; set; }
    public int PeerId { get; set; }
    public string? PeerName { get; set; }
    public MediaKind MediaKind { get; set; }
    public bool IsAdult { get; set; }
    public bool IsScene { get; set; }
    public PeerItemStatus LocalStatus { get; set; }
    public string? Title { get; set; }
    public int Year { get; set; }
    public int TvdbId { get; set; }
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public int SeasonNumber { get; set; }
    public string? SeriesType { get; set; }
    public List<int> EpisodeNumbers { get; set; } = new();
    public List<int> AbsoluteEpisodeNumbers { get; set; } = new();
    public string? EpisodeTitles { get; set; }
    public List<string> EpisodeNames { get; set; } = new();
    public List<string> AirDates { get; set; } = new();
    public int? SceneSeasonNumber { get; set; }
    public List<int> SceneEpisodeNumbers { get; set; } = new();

    // An album's artist and how many tracks it has.
    public string? Artist { get; set; }
    public int TrackCount { get; set; }
    public string? ReleaseTitle { get; set; }
    public string? Quality { get; set; }

    // Proper, Repack or Real, as the peer's Arr app recorded it. Null for a first release.
    public string? Revision { get; set; }
    public int Resolution { get; set; }
    public long Size { get; set; }
    public string? Languages { get; set; }
    public string? ReleaseGroup { get; set; }
    public DateTime? DateAdded { get; set; }

    // The file's BLAKE3 hash, where the node worked it out. The same hash from two nodes is the same file.
    public string? Blake3 { get; set; }
}

public class PeerCatalogTitleResource
{
    public string? Key { get; set; }
    public MediaKind MediaKind { get; set; }
    public bool IsAdult { get; set; }
    public bool IsScene { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public int Year { get; set; }
    public List<string> PeerNames { get; set; } = new();
    public int PosterItemId { get; set; }
    public int Files { get; set; }
    public int Missing { get; set; }
    public int Upgrade { get; set; }
    public int NotInLibrary { get; set; }
    public int Have { get; set; }
}

public class PeerCatalogSeriesResource
{
    public string? Key { get; set; }
    public string? Title { get; set; }
    public int Year { get; set; }
    public string? SeriesType { get; set; }
    public int PosterItemId { get; set; }
    public PeerItemStatus Status { get; set; }
    public int Episodes { get; set; }
    public int Missing { get; set; }
    public int Upgrade { get; set; }
    public long Size { get; set; }
    public List<PeerCatalogSeasonResource> Seasons { get; set; } = new();
}

public class PeerCatalogSeasonResource
{
    public int SeasonNumber { get; set; }
    public PeerItemStatus Status { get; set; }
    public int Episodes { get; set; }
    public int Missing { get; set; }
    public int Upgrade { get; set; }
    public long Size { get; set; }
    public List<PeerCatalogItemResource> Items { get; set; } = new();
}

[V5ApiController("catalog/peers")]
public class PeerCatalogController : Controller
{
    private static readonly HashSet<string> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "title",
        "size",
        "quality",
        "localStatus",
        "dateAdded"
    };

    private readonly IPeerCatalogService _peerCatalogService;
    private readonly ILocalHoldingsFactory _localHoldingsFactory;

    public PeerCatalogController(IPeerCatalogService peerCatalogService, ILocalHoldingsFactory localHoldingsFactory)
    {
        _peerCatalogService = peerCatalogService;
        _localHoldingsFactory = localHoldingsFactory;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<PeerCatalogSummaryResource>> GetSummaries()
    {
        return TypedResults.Ok(_peerCatalogService.GetSummaries().ConvertAll(s => new PeerCatalogSummaryResource
        {
            PeerId = s.PeerId,
            PeerName = s.PeerName,
            CatalogVersion = s.CatalogVersion,
            CatalogSyncedAt = s.CatalogSyncedAt,
            CatalogError = s.CatalogError,
            NotInLibrary = s.NotInLibrary,
            Missing = s.Missing,
            Upgrade = s.Upgrade,
            Have = s.Have
        }));
    }

    [HttpGet("details/{kind}/{itemId:int}")]
    [Produces("application/json")]
    public Results<Ok<CatalogDetailsResource>, NotFound> GetDetails(string kind, int itemId, [FromQuery] int? episodeIndex = null)
    {
        var item = _peerCatalogService.GetItem(itemId);
        if (item == null || item.MediaKind == MediaKind.Music)
        {
            return TypedResults.NotFound();
        }

        var summaries = _peerCatalogService.GetSummaries();
        var trusted = summaries.Select(s => s.PeerId).ToHashSet();

        if (kind == "episode")
        {
            var numbers = item.GetEpisodeNumbers();
            if (item.MediaKind != MediaKind.Tv || episodeIndex is not >= 0 || episodeIndex >= numbers.Count)
            {
                return TypedResults.NotFound();
            }

            var details = item.GetEpisodeDetails();
            var selected = episodeIndex.Value < details.Count ? details[episodeIndex.Value] : null;
            var absoluteNumbers = item.GetAbsoluteEpisodeNumbers();
            var airDates = item.GetAirDates();
            var localStatus = _localHoldingsFactory.Load().ClassifyEpisode(item, numbers[episodeIndex.Value]);
            return TypedResults.Ok(new CatalogDetailsResource
            {
                Kind = kind,
                Title = selected?.Title ?? (numbers.Count == 1 ? item.EpisodeTitles : null),
                ParentTitle = item.Title,
                SourceName = summaries.FirstOrDefault(s => s.PeerId == item.PeerId)?.PeerName,
                PosterPath = $"/catalog/posters/peer/{item.Id}",
                Year = item.Year,
                Overview = selected?.Overview,
                Genres = item.Genres,
                Network = item.Network,
                Runtime = item.Runtime,
                SeasonNumber = item.SeasonNumber,
                EpisodeNumber = numbers[episodeIndex.Value],
                AbsoluteEpisodeNumber = absoluteNumbers.Count == numbers.Count ? absoluteNumbers[episodeIndex.Value] : null,
                AirDate = airDates.Count == numbers.Count ? airDates[episodeIndex.Value] : null,
                TvdbId = item.TvdbId,
                TmdbId = item.TmdbId,
                ImdbId = item.ImdbId,
                Quality = item.Quality,
                Resolution = item.Resolution,
                Size = item.Size,
                LocalStatus = localStatus,
                GetIt = trusted.Contains(item.PeerId) && localStatus != PeerItemStatus.Have
                    ? new CatalogDetailsGetItResource { ItemId = item.Id, Scope = CatalogGetItScope.File, MediaKind = item.MediaKind }
                    : null
            });
        }

        var actualKind = item.MediaKind == MediaKind.Tv ? "series" :
            item.IsScene ? "scene" : item.IsAdult ? "film" : "movie";
        if (kind != actualKind)
        {
            return TypedResults.NotFound();
        }

        var seriesItems = kind == "series"
            ? _peerCatalogService.GetSeries(null)?.FirstOrDefault(s => s.Key == NzbDrone.Core.Posters.PeerPosters.KeyOf(item))?.Items ?? new List<PeerCatalogItem> { item }
            : new List<PeerCatalogItem>();
        var holdings = _localHoldingsFactory.Load();
        var getItem = (kind == "series" ? seriesItems : new List<PeerCatalogItem> { item })
            .FirstOrDefault(i => trusted.Contains(i.PeerId) && holdings.Classify(i) != PeerItemStatus.Have);

        return TypedResults.Ok(new CatalogDetailsResource
        {
            Kind = kind,
            Title = item.Title,
            SourceName = summaries.FirstOrDefault(s => s.PeerId == item.PeerId)?.PeerName,
            PosterPath = $"/catalog/posters/peer/{item.Id}",
            Year = item.Year,
            Overview = item.Overview,
            Genres = item.Genres,
            Network = item.Network,
            Runtime = item.Runtime,
            Studio = item.Studio,
            ReleaseDate = item.ReleaseDate,
            Code = item.Code,
            TvdbId = item.TvdbId,
            TmdbId = item.TmdbId,
            ImdbId = item.ImdbId,
            Quality = kind == "series" ? null : item.Quality,
            LocalStatus = kind == "series" ? null : holdings.Classify(item),
            Resolution = kind == "series" ? 0 : item.Resolution,
            Resolutions = seriesItems.Where(i => i.Resolution > 0).Select(i => i.Resolution).Distinct().OrderByDescending(r => r).ToList(),
            Episodes = seriesItems.Where(i => i.PeerId == item.PeerId)
                .SelectMany(i => i.GetEpisodeNumbers().Select((number, index) => new CatalogDetailsEpisodeResource
                {
                    SeasonNumber = i.SeasonNumber,
                    EpisodeNumber = number,
                    HasFile = true,
                    LocalStatus = holdings.ClassifyEpisode(i, number),
                    Title = i.GetEpisodeDetails().ElementAtOrDefault(index)?.Title ?? (i.GetEpisodeNumbers().Count == 1 ? i.EpisodeTitles : null),
                    Path = $"/catalog/peers/details/episode/{i.Id}?episodeIndex={index}"
                }))
                .GroupBy(e => (e.SeasonNumber, e.EpisodeNumber))
                .Select(g => g.First())
                .OrderBy(e => e.SeasonNumber == 0 ? int.MaxValue : e.SeasonNumber)
                .ThenBy(e => e.EpisodeNumber).ToList(),
            Size = kind == "series" ? 0 : item.Size,
            GetIt = getItem == null ? null : new CatalogDetailsGetItResource
            {
                // Keep the series anchor stable as individual episodes finish importing, so an
                // open Get it dialog can still find the command that started from this card.
                ItemId = trusted.Contains(item.PeerId) ? item.Id : getItem.Id,
                Scope = kind == "series" ? CatalogGetItScope.Title : CatalogGetItScope.File,
                MediaKind = item.MediaKind
            }
        });
    }

    // What friends offer by title, a page at a time, for browsing by poster.
    [HttpGet("titles")]
    [Produces("application/json")]
    public Ok<PagingResource<PeerCatalogTitleResource>> GetTitles([FromQuery] PagingRequestResource paging, [FromQuery] PeerItemStatus? status = null, [FromQuery] string? kind = null)
    {
        var names = _peerCatalogService.GetSummaries().ToDictionary(s => s.PeerId, s => s.PeerName);
        var titles = _peerCatalogService.GetTitles(status, kind);
        var pageSize = Math.Clamp(paging.PageSize ?? 50, 1, 250);
        var page = Math.Max(1, paging.Page ?? 1);

        return TypedResults.Ok(new PagingResource<PeerCatalogTitleResource>
        {
            Page = page,
            PageSize = pageSize,
            SortKey = "title",
            SortDirection = SortDirection.Ascending,
            TotalRecords = titles.Count,
            Records = titles.Skip((page - 1) * pageSize).Take(pageSize).Select(t => new PeerCatalogTitleResource
            {
                Key = t.Key,
                MediaKind = t.MediaKind,
                IsAdult = t.IsAdult,
                IsScene = t.IsScene,
                Title = t.Title,
                Artist = t.Artist,
                Year = t.Year,
                PeerNames = t.PeerIds.Select(p => names.GetValueOrDefault(p) ?? string.Empty).ToList(),
                PosterItemId = t.PosterItemId,
                Files = t.Files,
                Missing = t.Statuses.GetValueOrDefault(PeerItemStatus.Missing),
                Upgrade = t.Statuses.GetValueOrDefault(PeerItemStatus.Upgrade),
                NotInLibrary = t.Statuses.GetValueOrDefault(PeerItemStatus.NotInLibrary),
                Have = t.Statuses.GetValueOrDefault(PeerItemStatus.Have)
            }).ToList()
        });
    }

    [HttpGet("series")]
    [Produces("application/json")]
    public Ok<PagingResource<PeerCatalogSeriesResource>> GetSeries([FromQuery] PagingRequestResource paging, [FromQuery] PeerItemStatus? status = null)
    {
        var series = _peerCatalogService.GetSeries(status);
        var pageSize = Math.Clamp(paging.PageSize ?? 50, 1, 250);
        var page = Math.Max(1, paging.Page ?? 1);
        var names = _peerCatalogService.GetSummaries().ToDictionary(s => s.PeerId, s => s.PeerName);

        return TypedResults.Ok(new PagingResource<PeerCatalogSeriesResource>
        {
            Page = page,
            PageSize = pageSize,
            SortKey = "title",
            SortDirection = SortDirection.Ascending,
            TotalRecords = series.Count,
            Records = series.Skip((page - 1) * pageSize).Take(pageSize).Select(show =>
            {
                var seasons = show.Items.GroupBy(i => i.SeasonNumber)
                    .OrderBy(g => g.Key == 0 ? int.MaxValue : g.Key)
                    .Select(group =>
                    {
                        var items = group.OrderBy(i => show.SeriesType == "daily" ? i.GetAirDates().FirstOrDefault() : null, StringComparer.Ordinal)
                            .ThenBy(i => show.SeriesType == "anime" ? i.GetAbsoluteEpisodeNumbers().DefaultIfEmpty(i.GetEpisodeNumbers().DefaultIfEmpty(int.MaxValue).Min()).Min() : i.GetEpisodeNumbers().DefaultIfEmpty(int.MaxValue).Min())
                            .ThenBy(i => i.PeerId)
                            .ToList();
                        var episodeStatuses = PeerCatalogGrouping.EpisodeStatuses(items).Values.ToList();

                        return new PeerCatalogSeasonResource
                        {
                            SeasonNumber = group.Key,
                            Status = PeerCatalogGrouping.BestStatus(items),
                            Episodes = episodeStatuses.Count,
                            Missing = episodeStatuses.Count(s => s == PeerItemStatus.Missing),
                            Upgrade = episodeStatuses.Count(s => s == PeerItemStatus.Upgrade),
                            Size = items.Sum(i => i.Size),
                            Items = items.Select(item => ToResource(item, names)).ToList()
                        };
                    }).ToList();

                return new PeerCatalogSeriesResource
                {
                    Key = show.Key,
                    Title = show.Title,
                    Year = show.Year,
                    SeriesType = show.SeriesType,
                    PosterItemId = show.PosterItemId,
                    Status = PeerCatalogGrouping.BestStatus(show.Items),
                    Episodes = seasons.Sum(s => s.Episodes),
                    Missing = seasons.Sum(s => s.Missing),
                    Upgrade = seasons.Sum(s => s.Upgrade),
                    Size = seasons.Sum(s => s.Size),
                    Seasons = seasons
                };
            }).ToList()
        });
    }

    [HttpGet("items")]
    [Produces("application/json")]
    public Ok<PagingResource<PeerCatalogItemResource>> GetItems([FromQuery] PagingRequestResource paging, [FromQuery] int? peerId = null, [FromQuery] PeerItemStatus? status = null, [FromQuery] MediaKind? mediaKind = null, [FromQuery] string? kind = null)
    {
        var pagingSpec = new PagingResource<PeerCatalogItemResource>(paging)
            .MapToPagingSpec<PeerCatalogItemResource, PeerCatalogItem>(SortKeys, "title", SortDirection.Ascending);

        if (peerId.HasValue)
        {
            pagingSpec.FilterExpressions.Add(i => i.PeerId == peerId.Value);
        }

        if (status.HasValue)
        {
            pagingSpec.FilterExpressions.Add(i => i.LocalStatus == status.Value);
        }

        if (mediaKind.HasValue)
        {
            pagingSpec.FilterExpressions.Add(i => i.MediaKind == mediaKind.Value);
        }

        if (kind == "movies")
        {
            pagingSpec.FilterExpressions.Add(i => i.MediaKind == MediaKind.Movie && (i.ItemType == null || i.ItemType != "scene"));
        }
        else if (kind == "scenes")
        {
            pagingSpec.FilterExpressions.Add(i => i.IsAdult && i.ItemType == "scene");
        }
        else if (kind == "music")
        {
            pagingSpec.FilterExpressions.Add(i => i.MediaKind == MediaKind.Music);
        }

        var names = _peerCatalogService.GetSummaries().ToDictionary(s => s.PeerId, s => s.PeerName);

        return TypedResults.Ok(pagingSpec.ApplyToPage(_peerCatalogService.Paged, item => ToResource(item, names)));
    }

    private static PeerCatalogItemResource ToResource(PeerCatalogItem item, Dictionary<int, string> names)
    {
        return new PeerCatalogItemResource
        {
            Id = item.Id,
            PeerId = item.PeerId,
            PeerName = names.GetValueOrDefault(item.PeerId),
            MediaKind = item.MediaKind,
            IsAdult = item.IsAdult,
            IsScene = item.IsScene,
            LocalStatus = item.LocalStatus,
            Title = item.Title,
            Year = item.Year,
            TvdbId = item.TvdbId,
            TmdbId = item.TmdbId,
            ImdbId = item.ImdbId,
            SeasonNumber = item.SeasonNumber,
            SeriesType = item.SeriesType,
            EpisodeNumbers = item.GetEpisodeNumbers(),
            AbsoluteEpisodeNumbers = item.GetAbsoluteEpisodeNumbers(),
            EpisodeTitles = item.EpisodeTitles,
            EpisodeNames = item.GetEpisodeDetails().Select(e => e.Title ?? string.Empty).ToList(),
            AirDates = item.GetAirDates(),
            SceneSeasonNumber = item.SceneSeasonNumber,
            SceneEpisodeNumbers = item.GetSceneEpisodeNumbers(),
            Artist = item.Artist,
            TrackCount = item.IsAlbum ? item.GetTracks().Count : 0,
            ReleaseTitle = item.ReleaseTitle,
            Quality = item.Quality,
            Revision = RevisionLabel.For(item.RevisionVersion, item.RevisionReal, item.RevisionIsRepack),
            Resolution = item.Resolution,
            Size = item.Size,
            Languages = item.Languages,
            ReleaseGroup = item.ReleaseGroup,
            DateAdded = item.DateAdded,
            Blake3 = item.Blake3
        };
    }
}

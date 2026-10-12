using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.Extensions;
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
    public PeerItemStatus LocalStatus { get; set; }
    public string? Title { get; set; }
    public int Year { get; set; }
    public int TvdbId { get; set; }
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public int SeasonNumber { get; set; }
    public List<int> EpisodeNumbers { get; set; } = new();
    public string? EpisodeTitles { get; set; }
    public List<string> AirDates { get; set; } = new();
    public string? ReleaseTitle { get; set; }
    public string? Quality { get; set; }
    public int Resolution { get; set; }
    public long Size { get; set; }
    public string? Languages { get; set; }
    public string? ReleaseGroup { get; set; }
    public DateTime? DateAdded { get; set; }
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

    public PeerCatalogController(IPeerCatalogService peerCatalogService)
    {
        _peerCatalogService = peerCatalogService;
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

    [HttpGet("items")]
    [Produces("application/json")]
    public Ok<PagingResource<PeerCatalogItemResource>> GetItems([FromQuery] PagingRequestResource paging, [FromQuery] int? peerId = null, [FromQuery] PeerItemStatus? status = null, [FromQuery] MediaKind? mediaKind = null)
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

        var names = _peerCatalogService.GetSummaries().ToDictionary(s => s.PeerId, s => s.PeerName);

        return TypedResults.Ok(pagingSpec.ApplyToPage(_peerCatalogService.Paged, item => new PeerCatalogItemResource
        {
            Id = item.Id,
            PeerId = item.PeerId,
            PeerName = names.GetValueOrDefault(item.PeerId),
            MediaKind = item.MediaKind,
            LocalStatus = item.LocalStatus,
            Title = item.Title,
            Year = item.Year,
            TvdbId = item.TvdbId,
            TmdbId = item.TmdbId,
            ImdbId = item.ImdbId,
            SeasonNumber = item.SeasonNumber,
            EpisodeNumbers = item.GetEpisodeNumbers(),
            EpisodeTitles = item.EpisodeTitles,
            AirDates = item.GetAirDates(),
            ReleaseTitle = item.ReleaseTitle,
            Quality = item.Quality,
            Resolution = item.Resolution,
            Size = item.Size,
            Languages = item.Languages,
            ReleaseGroup = item.ReleaseGroup,
            DateAdded = item.DateAdded
        }));
    }
}

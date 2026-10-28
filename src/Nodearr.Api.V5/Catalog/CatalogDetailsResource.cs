using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace Nodearr.Api.V5.Catalog;

public class CatalogDetailsResource
{
    public string Kind { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? ParentTitle { get; set; }
    public string? SourceName { get; set; }
    public string? SentSummary { get; set; }
    public string? PosterPath { get; set; }
    public int Year { get; set; }
    public string? Overview { get; set; }
    public string? Genres { get; set; }
    public string? Network { get; set; }
    public int Runtime { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public int? AbsoluteEpisodeNumber { get; set; }
    public string? AirDate { get; set; }
    public string? Studio { get; set; }
    public string? ReleaseDate { get; set; }
    public string? Code { get; set; }
    public int TvdbId { get; set; }
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? Quality { get; set; }
    public int Resolution { get; set; }
    public List<int> Resolutions { get; set; } = new();
    public List<CatalogDetailsEpisodeResource> Episodes { get; set; } = new();
    public CatalogDetailsSendResource? Send { get; set; }
    public CatalogDetailsGetItResource? GetIt { get; set; }
    public PeerItemStatus? LocalStatus { get; set; }
    public bool? HasFile { get; set; }
    public long Size { get; set; }
}

public class CatalogDetailsEpisodeResource
{
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string? Title { get; set; }
    public string Path { get; set; } = string.Empty;
    public bool HasFile { get; set; }
    public bool WasSent { get; set; }
    public PeerItemStatus? LocalStatus { get; set; }
}

public class CatalogDetailsSendResource
{
    public string MediaKind { get; set; } = string.Empty;
    public int ApplicationId { get; set; }
    public int ArrId { get; set; }
    public int? EpisodeArrId { get; set; }
}

public class CatalogDetailsGetItResource
{
    public int ItemId { get; set; }
    public CatalogGetItScope Scope { get; set; }
    public MediaKind MediaKind { get; set; }
}

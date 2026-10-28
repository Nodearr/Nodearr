using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace Nodearr.Api.V5.Catalog;

public class MusicCatalogEntryResource
{
    public string Key { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? ParentTitle { get; set; }
    public string? SourceName { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? PosterPath { get; set; }
    public int Year { get; set; }
    public int AlbumCount { get; set; }
    public int TrackCount { get; set; }
    public PeerItemStatus? LocalStatus { get; set; }
    public CatalogDetailsSendResource? Send { get; set; }
    public CatalogDetailsGetItResource? GetIt { get; set; }
    public string? ForeignAlbumId { get; set; }
    public bool WasSent { get; set; }
}

[V5ApiController("catalog/music")]
public class MusicCatalogController : Controller
{
    private readonly ICatalogArtistRepository _artists;
    private readonly ICatalogAlbumRepository _albums;
    private readonly ICatalogTrackFileRepository _files;
    private readonly IApplicationFactory _applications;
    private readonly IPeerCatalogService _peers;
    private readonly ILocalHoldingsFactory _holdings;

    public MusicCatalogController(ICatalogArtistRepository artists, ICatalogAlbumRepository albums, ICatalogTrackFileRepository files, IApplicationFactory applications, IPeerCatalogService peers, ILocalHoldingsFactory holdings)
    {
        _artists = artists;
        _albums = albums;
        _files = files;
        _applications = applications;
        _peers = peers;
        _holdings = holdings;
    }

    [HttpGet("local")]
    public Ok<PagingResource<MusicCatalogEntryResource>> Local([FromQuery] PagingRequestResource paging, [FromQuery] string level = "artist")
    {
        var names = _applications.All().ToDictionary(a => a.Id, a => a.Name);
        var albums = _albums.All().Where(a => a.MissingSince == null).ToLookup(a => (a.ApplicationId, a.ArrArtistId));
        var files = _files.All().Where(f => f.MissingSince == null).ToLookup(f => (f.ApplicationId, f.ArrAlbumId));
        var entries = new List<MusicCatalogEntryResource>();
        foreach (var artist in _artists.All().Where(a => a.MissingSince == null && names.ContainsKey(a.ApplicationId)))
        {
            var children = albums[(artist.ApplicationId, artist.ArrId)]
                .Select(a => LocalAlbum(a, artist, files[(a.ApplicationId, a.ArrId)].ToList(), names[artist.ApplicationId])).ToList();
            if (level == "album")
            {
                entries.AddRange(children);
            }
            else
            {
                entries.Add(LocalArtist(artist, children, names[artist.ApplicationId]));
            }
        }

        return TypedResults.Ok(Page(entries, paging));
    }

    [HttpGet("local/{kind}/{applicationId:int}/{arrId:int}")]
    public Results<Ok<CatalogDetailsResource>, NotFound> LocalDetails(string kind, int applicationId, int arrId)
    {
        var album = kind == "album" ? _albums.ForArrIds(applicationId, new[] { arrId }).FirstOrDefault(a => a.MissingSince == null) : null;
        var artist = _artists.ForArrIds(applicationId, new[] { album?.ArrArtistId ?? arrId }).FirstOrDefault(a => a.MissingSince == null);
        var name = _applications.Find(applicationId)?.Name;
        if (artist == null || name == null || kind is not ("artist" or "album") || (kind == "album" && album == null))
        {
            return TypedResults.NotFound();
        }

        var files = _files.ForApplication(applicationId).Where(f => f.MissingSince == null && f.ArrArtistId == artist.ArrId).ToLookup(f => f.ArrAlbumId);
        if (album != null)
        {
            var albumFiles = files[album.ArrId].ToList();
            var entry = LocalAlbum(album, artist, albumFiles, name);
            var byId = albumFiles.ToDictionary(f => f.ArrId);
            var tracks = album.GetTracks();
            var listing = tracks.Count > 0 ? tracks.Select(t =>
            {
                var file = byId.GetValueOrDefault(t.TrackFileId);
                return new CatalogDetailsTrackResource { Title = t.Title, Number = t.Number, Disc = t.Disc, Duration = t.Duration, HasFile = file != null, Quality = file?.Quality, Size = file?.Size ?? 0 };
            }).ToList() : albumFiles.OrderBy(f => f.DiscNumber).ThenBy(f => f.TrackNumber)
                .Select(f => new CatalogDetailsTrackResource { Title = f.TrackTitle, Number = f.TrackNumber, Disc = f.DiscNumber, HasFile = true, Quality = f.Quality, Size = f.Size }).ToList();
            return TypedResults.Ok(new CatalogDetailsResource
            {
                Kind = kind, Title = album.Title, ParentTitle = artist.Name, ParentPath = LocalPath("artist", applicationId, artist.ArrId),
                SourceName = name, PosterPath = entry.PosterPath, Year = album.Year, Overview = album.Overview, Genres = album.Genres,
                ReleaseDate = album.ReleaseDate, AlbumType = album.AlbumType, ForeignArtistId = artist.ForeignArtistId, ForeignAlbumId = album.ForeignAlbumId,
                LocalStatus = entry.LocalStatus, Size = albumFiles.Sum(f => f.Size), Tracks = listing, Send = entry.Send
            });
        }

        var children = _albums.ForApplication(applicationId).Where(a => a.MissingSince == null && a.ArrArtistId == artist.ArrId)
            .OrderBy(a => a.Year).ThenBy(a => a.Title).Select(a => LocalAlbum(a, artist, files[a.ArrId].ToList(), name)).ToList();
        return TypedResults.Ok(new CatalogDetailsResource
        {
            Kind = kind, Title = artist.Name, SourceName = name, Overview = artist.Overview, Genres = artist.Genres,
            PosterPath = $"/catalog/posters/local/{applicationId}/artist/{artist.ArrId}", ForeignArtistId = artist.ForeignArtistId,
            Albums = children, Send = LocalArtist(artist, children, name).Send
        });
    }

    [HttpGet("peer")]
    public Ok<PagingResource<MusicCatalogEntryResource>> Peer([FromQuery] PagingRequestResource paging, [FromQuery] string level = "artist", [FromQuery] PeerItemStatus? status = null)
    {
        var items = _peers.GetMusic();
        var holdings = _holdings.Load();
        var names = _peers.GetSummaries().ToDictionary(p => p.PeerId, p => p.PeerName);
        foreach (var item in items)
        {
            item.LocalStatus = holdings.Classify(item);
        }

        var entries = level == "album"
            ? items.Where(i => !status.HasValue || i.LocalStatus == status).Select(i => MusicCatalogDetails.Album(i, names.GetValueOrDefault(i.PeerId))).ToList()
            : items.GroupBy(i => (i.PeerId, Artist: MusicCatalogDetails.ArtistKey(i)))
                .Where(g => !status.HasValue || g.Any(i => i.LocalStatus == status))
                .Select(g => MusicCatalogDetails.Artist(g.ToList(), names.GetValueOrDefault(g.Key.PeerId))).ToList();
        return TypedResults.Ok(Page(entries, paging));
    }

    [HttpGet("peer/{kind}/{itemId:int}")]
    public Results<Ok<CatalogDetailsResource>, NotFound> PeerDetails(string kind, int itemId)
    {
        var resource = MusicCatalogDetails.Get(_peers, _holdings, kind, itemId);
        return resource == null ? TypedResults.NotFound() : TypedResults.Ok(resource);
    }

    private static string LocalPath(string kind, int app, int id) => $"/catalog/music/local/{kind}/{app}/{id}";

    private static PagingResource<MusicCatalogEntryResource> Page(List<MusicCatalogEntryResource> entries, PagingRequestResource paging)
    {
        var page = Math.Max(paging.Page ?? 1, 1);
        var size = Math.Clamp(paging.PageSize ?? 48, 1, 250);
        return new PagingResource<MusicCatalogEntryResource>
        {
            Page = page, PageSize = size, TotalRecords = entries.Count,
            Records = entries.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Key).Skip((page - 1) * size).Take(size).ToList()
        };
    }

    private static MusicCatalogEntryResource LocalAlbum(CatalogAlbum album, CatalogArtist artist, List<CatalogTrackFile> files, string? source)
    {
        var complete = album.IsWhole && files.Count >= album.TrackCount && files.All(f => f.Size > 0);
        return new MusicCatalogEntryResource
        {
            Key = $"album-{album.ApplicationId}-{album.ArrId}", Title = album.Title, ParentTitle = artist.Name, SourceName = source,
            ForeignAlbumId = album.ForeignAlbumId,
            Path = LocalPath("album", album.ApplicationId, album.ArrId), PosterPath = $"/catalog/posters/local/{album.ApplicationId}/album/{album.ArrId}",
            Year = album.Year, TrackCount = album.TrackCount, LocalStatus = complete ? PeerItemStatus.Have : PeerItemStatus.Missing,
            Send = complete ? new CatalogDetailsSendResource { MediaKind = "music", ApplicationId = album.ApplicationId, ArrId = artist.ArrId, AlbumArrId = album.ArrId } : null
        };
    }

    private static MusicCatalogEntryResource LocalArtist(CatalogArtist artist, List<MusicCatalogEntryResource> albums, string? source) => new()
    {
        Key = $"artist-{artist.ApplicationId}-{artist.ArrId}", Title = artist.Name, SourceName = source,
        Path = LocalPath("artist", artist.ApplicationId, artist.ArrId), PosterPath = $"/catalog/posters/local/{artist.ApplicationId}/artist/{artist.ArrId}",
        AlbumCount = albums.Count, TrackCount = albums.Sum(a => a.TrackCount),
        Send = albums.Any(a => a.Send != null) ? new CatalogDetailsSendResource { MediaKind = "music", ApplicationId = artist.ApplicationId, ArrId = artist.ArrId } : null
    };
}

public static class MusicCatalogDetails
{
    public static string ArtistKey(PeerCatalogItem item) => string.IsNullOrEmpty(item.ForeignArtistId) ? item.RemoteId : item.ForeignArtistId.ToLowerInvariant();

    public static MusicCatalogEntryResource Album(PeerCatalogItem item, string? source) => new()
    {
        Key = $"album-{item.Id}", Title = item.Title, ParentTitle = item.Artist, SourceName = source,
        ForeignAlbumId = item.ForeignAlbumId,
        Path = $"/catalog/music/peer/album/{item.Id}", PosterPath = $"/catalog/posters/peer/{item.Id}", Year = item.Year,
        TrackCount = item.GetTracks().Count, LocalStatus = item.LocalStatus,
        GetIt = item.LocalStatus == PeerItemStatus.Have ? null : new CatalogDetailsGetItResource { ItemId = item.Id, Scope = CatalogGetItScope.File, MediaKind = MediaKind.Music }
    };

    public static MusicCatalogEntryResource Artist(List<PeerCatalogItem> items, string? source)
    {
        var first = items.OrderBy(i => i.Id).First();
        return new MusicCatalogEntryResource
        {
            Key = $"artist-{first.PeerId}-{ArtistKey(first)}", Title = first.Artist, SourceName = source,
            Path = $"/catalog/music/peer/artist/{first.Id}", AlbumCount = items.Count, TrackCount = items.Sum(i => i.GetTracks().Count),
            GetIt = items.All(i => i.LocalStatus == PeerItemStatus.Have) || string.IsNullOrEmpty(first.ForeignArtistId) ? null :
                new CatalogDetailsGetItResource { ItemId = first.Id, Scope = CatalogGetItScope.Artist, MediaKind = MediaKind.Music }
        };
    }

    public static CatalogDetailsResource? Get(IPeerCatalogService peers, ILocalHoldingsFactory factory, string kind, int itemId)
    {
        if (kind is not ("artist" or "album"))
        {
            return null;
        }

        // Only current offers from this trusted sender. Grouping by artist name would leak unrelated albums.
        var music = peers.GetMusic() ?? new();
        var item = music.FirstOrDefault(i => i.Id == itemId);
        var source = item == null ? null : peers.GetSummaries().FirstOrDefault(p => p.PeerId == item.PeerId);
        if (item == null || source == null)
        {
            return null;
        }

        var holdings = factory.Load();
        var albums = music.Where(i => i.PeerId == item.PeerId && ArtistKey(i) == ArtistKey(item)).OrderBy(i => i.Year).ThenBy(i => i.Title).ToList();
        foreach (var album in albums)
        {
            album.LocalStatus = holdings.Classify(album);
        }

        var entry = kind == "artist" ? Artist(albums, source.PeerName) : Album(item, source.PeerName);
        return new CatalogDetailsResource
        {
            Kind = kind, Title = entry.Title, ParentTitle = kind == "album" ? item.Artist : null,
            ParentPath = kind == "album" ? $"/catalog/music/peer/artist/{item.Id}" : null,
            SourceName = source.PeerName, PosterPath = entry.PosterPath, Year = kind == "album" ? item.Year : 0,
            Overview = kind == "artist" ? item.ArtistOverview : item.Overview, Genres = kind == "artist" ? item.ArtistGenres : item.Genres,
            ForeignArtistId = item.ForeignArtistId, ForeignAlbumId = kind == "album" ? item.ForeignAlbumId : null,
            ReleaseDate = kind == "album" ? item.ReleaseDate : null, AlbumType = kind == "album" ? item.AlbumType : null,
            Quality = kind == "album" ? item.Quality : null, Size = kind == "album" ? item.Size : 0,
            LocalStatus = kind == "album" ? item.LocalStatus : null, GetIt = entry.GetIt,
            Albums = kind == "artist" ? albums.Select(i => Album(i, source.PeerName)).ToList() : new(),
            Tracks = kind == "album" ? item.GetTracks().Select(t => new CatalogDetailsTrackResource
            {
                Title = t.Title, Number = t.Number ?? 0, Disc = t.Disc ?? 0, Duration = t.Duration ?? 0,
                HasFile = true, LocalStatus = holdings.ClassifyTrack(item, t), Quality = t.Quality, Size = t.Size
            }).ToList() : new()
        };
    }
}

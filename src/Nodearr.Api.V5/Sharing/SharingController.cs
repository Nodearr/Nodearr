using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Transfers;

namespace Nodearr.Api.V5.Sharing;

public class TitleKeyResource
{
    public MediaKind MediaKind { get; set; }
    public int ApplicationId { get; set; }
    public int ArrId { get; set; }
}

// A series, a season of it, one episode of it, or a movie. ArrId is the series or the movie.
public class ShareKeyResource : TitleKeyResource
{
    public int? SeasonNumber { get; set; }
    public int? EpisodeArrId { get; set; }

    public ShareKey ToModel()
    {
        return new ShareKey(MediaKind, ApplicationId, ArrId, SeasonNumber, EpisodeArrId);
    }

    public static ShareKeyResource FromModel(ShareKey key)
    {
        return new ShareKeyResource { MediaKind = key.Kind, ApplicationId = key.ApplicationId, ArrId = key.ArrId, SeasonNumber = key.SeasonNumber, EpisodeArrId = key.EpisodeArrId };
    }
}

public class ShareableSeasonResource
{
    public int SeasonNumber { get; set; }
    public List<ShareableEpisodeResource> Episodes { get; set; } = new();
}

public class ShareableEpisodeResource
{
    public int EpisodeArrId { get; set; }
    public int EpisodeNumber { get; set; }
    public string? Title { get; set; }
    public bool HasFile { get; set; }
}

public class ShareListResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool IsHidden { get; set; }
    public List<ShareKeyResource> Items { get; set; } = new();
    public List<int> PeerIds { get; set; } = new();

    public static ShareListResource FromModel(ShareListDetails details)
    {
        return new ShareListResource
        {
            Id = details.List.Id,
            Name = details.List.Name,
            IsHidden = details.List.IsHidden,
            Items = details.Items.ConvertAll(ShareKeyResource.FromModel),
            PeerIds = details.PeerIds
        };
    }
}

public class ShareableTitleResource : TitleKeyResource
{
    public string? ApplicationName { get; set; }
    public string? Title { get; set; }
    public int Year { get; set; }
    public int Files { get; set; }
}

public class HashingStatusResource
{
    public int Shared { get; set; }
    public int Hashed { get; set; }
    public string? Current { get; set; }
    public bool WaitingForUploads { get; set; }
    public DateTime? LastPass { get; set; }
}

[V5ApiController("sharing")]
public class SharingController : Controller
{
    private readonly ISharingService _sharingService;
    private readonly IFileHasher _hasher;

    public SharingController(ISharingService sharingService, IFileHasher hasher)
    {
        _sharingService = sharingService;
        _hasher = hasher;
    }

    // How far the background hashing of shared files has got.
    [HttpGet("hashing")]
    [Produces("application/json")]
    public Ok<HashingStatusResource> GetHashing()
    {
        var status = _hasher.Status();

        return TypedResults.Ok(new HashingStatusResource
        {
            Shared = status.Shared,
            Hashed = status.Hashed,
            Current = status.Current,
            WaitingForUploads = status.WaitingForUploads,
            LastPass = status.LastPass
        });
    }

    [HttpGet("lists")]
    [Produces("application/json")]
    public Ok<List<ShareListResource>> GetLists()
    {
        return TypedResults.Ok(_sharingService.All().ConvertAll(ShareListResource.FromModel));
    }

    [HttpPost("lists")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<ShareListResource> Create([FromBody] ShareListResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            throw new BadRequestException("A share list needs a name");
        }

        var created = _sharingService.Create(resource.Name);

        return TypedResults.Ok(ShareListResource.FromModel(_sharingService.Update(created.List.Id, null, resource.Items.Select(i => i.ToModel()), resource.PeerIds)));
    }

    [HttpPut("lists/{id:int}")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<ShareListResource> Update(int id, [FromBody] ShareListResource resource)
    {
        return TypedResults.Ok(ShareListResource.FromModel(_sharingService.Update(id, resource.Name, resource.Items.Select(i => i.ToModel()), resource.PeerIds)));
    }

    [HttpDelete("lists/{id:int}")]
    public NoContent Delete(int id)
    {
        try
        {
            _sharingService.Delete(id);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        return TypedResults.NoContent();
    }

    // The seasons and episodes of one series, for choosing among them.
    [HttpGet("titles/{applicationId:int}/{arrId:int}/seasons")]
    [Produces("application/json")]
    public Ok<List<ShareableSeasonResource>> GetSeasons(int applicationId, int arrId)
    {
        return TypedResults.Ok(_sharingService.GetSeasons(applicationId, arrId).ConvertAll(s => new ShareableSeasonResource
        {
            SeasonNumber = s.SeasonNumber,
            Episodes = s.Episodes.ConvertAll(e => new ShareableEpisodeResource { EpisodeArrId = e.EpisodeArrId, EpisodeNumber = e.EpisodeNumber, Title = e.Title, HasFile = e.HasFile })
        }));
    }

    // Every series and movie in the local catalog, for choosing what goes on a list.
    [HttpGet("titles")]
    [Produces("application/json")]
    public Ok<List<ShareableTitleResource>> GetTitles()
    {
        return TypedResults.Ok(_sharingService.GetTitles().ConvertAll(t => new ShareableTitleResource
        {
            MediaKind = t.Key.Kind,
            ApplicationId = t.Key.ApplicationId,
            ArrId = t.Key.ArrId,
            ApplicationName = t.ApplicationName,
            Title = t.Title,
            Year = t.Year,
            Files = t.Files
        }));
    }
}

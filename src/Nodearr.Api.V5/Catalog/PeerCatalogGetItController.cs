using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Api.V5.Commands;
using Nodearr.Api.V5.Sharing;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace Nodearr.Api.V5.Catalog;

public class CatalogGetItPlanResource
{
    public GetItPlanResource Plan { get; set; } = new();
    public List<int> Seasons { get; set; } = new();
    public List<CatalogGetItFileResource> Files { get; set; } = new();
}

public class CatalogGetItFileResource
{
    public int Id { get; set; }
    public int SeasonNumber { get; set; }
    public List<int> EpisodeNumbers { get; set; } = new();
    public string? Quality { get; set; }
    public PeerItemStatus LocalStatus { get; set; }
}

public class CatalogGetItResource
{
    public CatalogGetItScope Scope { get; set; }
    public int? SeasonNumber { get; set; }
    public GetItOptionsResource Options { get; set; } = new();
}

[V5ApiController("catalog/peers/items")]
public class PeerCatalogGetItController : Controller
{
    private readonly IPeerCatalogGetItService _getIt;

    public PeerCatalogGetItController(IPeerCatalogGetItService getIt)
    {
        _getIt = getIt;
    }

    [HttpGet("{id:int}/getit")]
    [Produces("application/json")]
    public Ok<CatalogGetItPlanResource> Plan(int id, [FromQuery] CatalogGetItScope scope = CatalogGetItScope.File, [FromQuery] int? seasonNumber = null)
    {
        try
        {
            var plan = _getIt.Plan(id, scope, seasonNumber);

            return TypedResults.Ok(new CatalogGetItPlanResource
            {
                Plan = GetItPlanResource.FromModel(plan.Plan),
                Seasons = plan.Seasons,
                Files = plan.Files.Select(i => new CatalogGetItFileResource
                {
                    Id = i.Id,
                    SeasonNumber = i.SeasonNumber,
                    EpisodeNumbers = i.GetEpisodeNumbers(),
                    Quality = i.Quality,
                    LocalStatus = i.LocalStatus
                }).ToList()
            });
        }
        catch (GetItException ex)
        {
            throw new BadRequestException(ex.Message);
        }
    }

    [HttpPost("{id:int}/getit")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<CommandResource> GetIt(int id, [FromBody] CatalogGetItResource resource)
    {
        try
        {
            return TypedResults.Ok(_getIt.GetIt(id, resource.Scope, resource.SeasonNumber, resource.Options.ToModel()).ToResource());
        }
        catch (GetItException ex)
        {
            throw new BadRequestException(ex.Message);
        }
    }
}

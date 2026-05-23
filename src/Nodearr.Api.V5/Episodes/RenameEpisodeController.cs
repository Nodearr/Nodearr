using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.MediaFiles;

namespace Nodearr.Api.V5.Episodes;

[V5ApiController("rename")]
public class RenameEpisodeController : Controller
{
    private readonly IRenameEpisodeFileService _renameEpisodeFileService;

    public RenameEpisodeController(IRenameEpisodeFileService renameEpisodeFileService)
    {
        _renameEpisodeFileService = renameEpisodeFileService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<RenameEpisodeResource>> GetEpisodes(int seriesId, int? seasonNumber)
    {
        if (seasonNumber.HasValue)
        {
            return TypedResults.Ok(_renameEpisodeFileService.GetRenamePreviews(seriesId, seasonNumber.Value).ToResource());
        }

        return TypedResults.Ok(_renameEpisodeFileService.GetRenamePreviews(seriesId).ToResource());
    }

    [HttpGet("bulk")]
    [Produces("application/json")]
    public Results<Ok<List<RenameEpisodeResource>>, BadRequest> GetEpisodes([FromQuery] List<int> seriesIds)
    {
        if (seriesIds is { Count: 0 })
        {
            throw new BadRequestException("seriesIds must be provided");
        }

        if (seriesIds.Any(seriesId => seriesId <= 0))
        {
            throw new BadRequestException("seriesIds must be positive integers");
        }

        return TypedResults.Ok(_renameEpisodeFileService.GetRenamePreviews(seriesIds).ToResource());
    }
}

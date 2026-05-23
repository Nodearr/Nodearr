using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.Extensions;
using Nodearr.Http.REST.Attributes;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;

namespace Nodearr.Api.V5.Blocklist;

[V5ApiController]
public class BlocklistController : Controller
{
    private readonly IBlocklistService _blocklistService;
    private readonly ICustomFormatCalculationService _formatCalculator;

    public BlocklistController(IBlocklistService blocklistService,
                               ICustomFormatCalculationService formatCalculator)
    {
        _blocklistService = blocklistService;
        _formatCalculator = formatCalculator;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<PagingResource<BlocklistResource>> GetBlocklist([FromQuery] PagingRequestResource paging, [FromQuery] int[]? seriesIds = null, [FromQuery] DownloadProtocol[]? protocols = null)
    {
        var pagingResource = new PagingResource<BlocklistResource>(paging);
        var pagingSpec = pagingResource.MapToPagingSpec<BlocklistResource, NzbDrone.Core.Blocklisting.Blocklist>(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "date",
                "indexer",
                "series.sortTitle",
                "sourceTitle"
            },
            "date",
            SortDirection.Descending);

        if (seriesIds?.Any() == true)
        {
            pagingSpec.FilterExpressions.Add(b => seriesIds.Contains(b.SeriesId));
        }

        if (protocols?.Any() == true)
        {
            pagingSpec.FilterExpressions.Add(b => protocols.Contains(b.Protocol));
        }

        return TypedResults.Ok(pagingSpec.ApplyToPage(b => _blocklistService.Paged(pagingSpec), b => BlocklistResourceMapper.MapToResource(b, _formatCalculator)));
    }

    [RestDeleteById]
    public NoContent DeleteBlocklist(int id)
    {
        _blocklistService.Delete(id);

        return TypedResults.NoContent();
    }

    [HttpDelete("bulk")]
    [Produces("application/json")]
    public NoContent Remove([FromBody] BlocklistBulkResource resource)
    {
        _blocklistService.Delete(resource.Ids);

        return TypedResults.NoContent();
    }
}

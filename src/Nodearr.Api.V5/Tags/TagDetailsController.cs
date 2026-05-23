using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.Tags;

namespace Nodearr.Api.V5.Tags;

[V5ApiController("tag/detail")]
public class TagDetailsController : RestController<TagDetailsResource>
{
    private readonly ITagService _tagService;

    public TagDetailsController(ITagService tagService)
    {
        _tagService = tagService;
    }

    protected override TagDetailsResource GetResourceById(int id)
    {
        return _tagService.Details(id).ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<TagDetailsResource>> GetAll()
    {
        return TypedResults.Ok(_tagService.Details().ToResource());
    }
}

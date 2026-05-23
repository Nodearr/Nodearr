using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.Tags;

namespace Nodearr.Api.V3.Tags
{
    [V3ApiController("tag/detail")]
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
        public List<TagDetailsResource> GetAll()
        {
            return _tagService.Details().ToResource();
        }
    }
}

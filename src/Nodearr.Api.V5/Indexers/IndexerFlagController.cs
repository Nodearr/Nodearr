using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Parser.Model;

namespace Nodearr.Api.V5.Indexers;

[V5ApiController]
public class IndexerFlagController : Controller
{
    [HttpGet]
    public Ok<List<IndexerFlagResource>> GetAll()
    {
        return TypedResults.Ok(Enum.GetValues(typeof(IndexerFlags)).Cast<IndexerFlags>().Select(f => new IndexerFlagResource
        {
            Id = (int)f,
            Name = f.ToString()
        }).ToList());
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Parser.Model;

namespace Nodearr.Api.V3.Indexers
{
    [V3ApiController]
    public class IndexerFlagController : Controller
    {
        [HttpGet]
        public List<IndexerFlagResource> GetAll()
        {
            return Enum.GetValues(typeof(IndexerFlags)).Cast<IndexerFlags>().Select(f => new IndexerFlagResource
            {
                Id = (int)f,
                Name = f.ToString()
            }).ToList();
        }
    }
}

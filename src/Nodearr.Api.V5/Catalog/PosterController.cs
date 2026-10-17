using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Posters;

namespace Nodearr.Api.V5.Catalog;

// Posters for the web UI's pages. An <img> cannot send the API key, so these are signed in to as
// the UI's own pages are.
[V5ApiController("catalog/posters")]
[Authorize(Policy = "UI")]
public class PosterController : Controller
{
    private readonly IPeerPosters _peerPosters;

    public PosterController(IPeerPosters peerPosters)
    {
        _peerPosters = peerPosters;
    }

    // The poster of the title a friend's catalog item belongs to, from this node's copy or the friend.
    [HttpGet("peer/{id:int}")]
    [Produces("image/jpeg", "image/png")]
    public IActionResult GetPeerPoster(int id)
    {
        var poster = _peerPosters.Get(id);

        if (poster == null)
        {
            return NotFound();
        }

        // The browser keeps it for a day. Nodearr keeps its own copy longer.
        Response.Headers.CacheControl = "private, max-age=86400";

        return File(poster, PosterImages.ContentType(poster));
    }
}

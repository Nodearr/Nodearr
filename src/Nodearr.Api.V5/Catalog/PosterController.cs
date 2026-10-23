using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Posters;
using NzbDrone.Core.Sharing;

namespace Nodearr.Api.V5.Catalog;

// Posters for the web UI's pages. An <img> cannot send the API key, so these are signed in to as
// the UI's own pages are.
[V5ApiController("catalog/posters")]
[Authorize(Policy = "UI")]
public class PosterController : Controller
{
    private readonly IPeerPosters _peerPosters;
    private readonly ILocalPosters _localPosters;

    public PosterController(IPeerPosters peerPosters, ILocalPosters localPosters)
    {
        _peerPosters = peerPosters;
        _localPosters = localPosters;
    }

    // The poster of a series, movie or album in this node's own library, from its own Arr app, as
    // a friend is sent it. Nothing else is contacted.
    [HttpGet("local/{applicationId:int}/{subject}/{arrId:int}")]
    [Produces("image/jpeg", "image/png")]
    public IActionResult GetLocalPoster(int applicationId, string subject, int arrId)
    {
        if (!Enum.TryParse<PosterSubject>(subject, true, out var posterSubject) || !Enum.IsDefined(posterSubject))
        {
            return NotFound();
        }

        return Image(_localPosters.Get(new PosterTitle(applicationId, posterSubject, arrId)));
    }

    // The poster of the title a friend's catalog item belongs to, from this node's copy or the friend.
    [HttpGet("peer/{id:int}")]
    [Produces("image/jpeg", "image/png")]
    public IActionResult GetPeerPoster(int id)
    {
        return Image(_peerPosters.Get(id));
    }

    private IActionResult Image(byte[] poster)
    {
        if (poster == null)
        {
            return NotFound();
        }

        // The browser keeps it for a day. Nodearr keeps its own copy longer.
        Response.Headers.CacheControl = "private, max-age=86400";

        return File(poster, PosterImages.ContentType(poster));
    }
}

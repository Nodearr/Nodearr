using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Catalog;

namespace Nodearr.Api.V5.Catalog;

// Where Sonarr and Radarr send their webhook. Nodearr adds the webhook to each Arr app itself, with
// this node's API key, alongside its indexer and download client.
[V5ApiController("arr/webhook")]
public class ArrWebhookController : Controller
{
    private readonly IArrWebhookService _webhooks;

    public ArrWebhookController(IArrWebhookService webhooks)
    {
        _webhooks = webhooks;
    }

    [HttpPost("{applicationId:int}")]
    public async Task<Results<Accepted, BadRequest>> Receive(int applicationId)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        return _webhooks.Receive(applicationId, body) ? TypedResults.Accepted((string?)null) : TypedResults.BadRequest();
    }
}

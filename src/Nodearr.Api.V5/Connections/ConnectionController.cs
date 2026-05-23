using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Api.V5.Provider;
using Nodearr.Http;
using NzbDrone.Core.Notifications;
using NzbDrone.SignalR;

namespace Nodearr.Api.V5.Connections;

[V5ApiController]
public class ConnectionController : ProviderControllerBase<ConnectionResource, ConnectionBulkResource, INotification, NotificationDefinition>
{
    public static readonly ConnectionResourceMapper ResourceMapper = new();
    public static readonly ConnectionBulkResourceMapper BulkResourceMapper = new();

    public ConnectionController(IBroadcastSignalRMessage signalRBroadcaster, NotificationFactory notificationFactory)
        : base(signalRBroadcaster, notificationFactory, "connection", ResourceMapper, BulkResourceMapper)
    {
    }

    [NonAction]
    public override Results<Ok<IEnumerable<ConnectionResource>>, BadRequest> UpdateProvider([FromBody] ConnectionBulkResource providerResource)
    {
        throw new NotImplementedException();
    }

    [NonAction]
    public override NoContent DeleteProviders([FromBody] ConnectionBulkResource resource)
    {
        throw new NotImplementedException();
    }
}

using Nodearr.Api.V5.Provider;
using NzbDrone.Core.Notifications;

namespace Nodearr.Api.V5.Connections;

public class ConnectionBulkResource : ProviderBulkResource<ConnectionBulkResource>
{
}

public class ConnectionBulkResourceMapper : ProviderBulkResourceMapper<ConnectionBulkResource, NotificationDefinition>
{
}

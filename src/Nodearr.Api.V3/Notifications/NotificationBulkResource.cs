using NzbDrone.Core.Notifications;

namespace Nodearr.Api.V3.Notifications
{
    public class NotificationBulkResource : ProviderBulkResource<NotificationBulkResource>
    {
    }

    public class NotificationBulkResourceMapper : ProviderBulkResourceMapper<NotificationBulkResource, NotificationDefinition>
    {
    }
}

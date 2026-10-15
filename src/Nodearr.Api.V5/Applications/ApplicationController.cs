using Nodearr.Api.V5.Provider;
using Nodearr.Http;
using NzbDrone.Core.Applications;
using NzbDrone.SignalR;

namespace Nodearr.Api.V5.Applications
{
    [V5ApiController("applications")]
    public class ApplicationController : ProviderControllerBase<ApplicationResource, ApplicationBulkResource, IApplication, ApplicationDefinition>
    {
        public static readonly ApplicationResourceMapper ResourceMapper = new();
        public static readonly ApplicationBulkResourceMapper BulkResourceMapper = new();

        public ApplicationController(IBroadcastSignalRMessage signalRBroadcaster, IApplicationFactory applicationsFactory)
            : base(signalRBroadcaster, applicationsFactory, "applications", ResourceMapper, BulkResourceMapper)
        {
        }
    }
}

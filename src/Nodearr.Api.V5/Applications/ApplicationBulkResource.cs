using Nodearr.Api.V5.Provider;
using NzbDrone.Core.Applications;

namespace Nodearr.Api.V5.Applications
{
    public class ApplicationBulkResource : ProviderBulkResource<ApplicationBulkResource>
    {
        public ApplicationSyncLevel? SyncLevel { get; set; }
    }

    public class ApplicationBulkResourceMapper : ProviderBulkResourceMapper<ApplicationBulkResource, ApplicationDefinition>
    {
        public override List<ApplicationDefinition> UpdateModel(ApplicationBulkResource resource, List<ApplicationDefinition> existingDefinitions)
        {
            existingDefinitions.ForEach(existing =>
            {
                existing.SyncLevel = resource.SyncLevel ?? existing.SyncLevel;
            });

            return existingDefinitions;
        }
    }
}

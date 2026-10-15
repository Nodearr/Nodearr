using Nodearr.Api.V5.Provider;
using NzbDrone.Core.Applications;
using Swashbuckle.AspNetCore.Annotations;

namespace Nodearr.Api.V5.Applications
{
    public class ApplicationResource : ProviderResource<ApplicationResource>
    {
        public ApplicationSyncLevel SyncLevel { get; set; }

        [SwaggerIgnore]
        public bool Enable { get; set; }

        public string? TestCommand { get; set; }
    }

    public class ApplicationResourceMapper : ProviderResourceMapper<ApplicationResource, ApplicationDefinition>
    {
        public override ApplicationResource ToResource(ApplicationDefinition definition)
        {
            var resource = base.ToResource(definition);
            resource.SyncLevel = definition.SyncLevel;
            resource.Enable = definition.Enable;

            return resource;
        }

        public override ApplicationDefinition ToModel(ApplicationResource resource, ApplicationDefinition? existingDefinition)
        {
            var definition = base.ToModel(resource, existingDefinition);
            definition.SyncLevel = resource.SyncLevel;

            return definition;
        }
    }
}

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Api.V5.Provider;
using Nodearr.Http;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.SignalR;

namespace Nodearr.Api.V5.Metadata;

[V5ApiController]
public class MetadataController : ProviderControllerBase<MetadataResource, MetadataBulkResource, IMetadata, MetadataDefinition>
{
    public static readonly MetadataResourceMapper ResourceMapper = new();
    public static readonly MetadataBulkResourceMapper BulkResourceMapper = new();

    public MetadataController(IBroadcastSignalRMessage signalRBroadcaster, IMetadataFactory metadataFactory)
        : base(signalRBroadcaster, metadataFactory, "metadata", ResourceMapper, BulkResourceMapper)
    {
    }

    [NonAction]
    public override Results<Ok<IEnumerable<MetadataResource>>, BadRequest> UpdateProvider([FromBody] MetadataBulkResource providerResource)
    {
        throw new NotImplementedException();
    }

    [NonAction]
    public override NoContent DeleteProviders([FromBody] MetadataBulkResource resource)
    {
        throw new NotImplementedException();
    }
}

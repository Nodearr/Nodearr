using Nodearr.Http.REST;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;

namespace Nodearr.Api.V5.Settings;

public class ImportListSettingsResource : RestResource
{
    public ListSyncLevelType ListSyncLevel { get; set; }
    public int ListSyncTag { get; set; }
}

public static class ImportListSettingsResourceMapper
{
    public static ImportListSettingsResource ToResource(IConfigService model)
    {
        return new ImportListSettingsResource
        {
            ListSyncLevel = model.ListSyncLevel,
            ListSyncTag = model.ListSyncTag,
        };
    }
}

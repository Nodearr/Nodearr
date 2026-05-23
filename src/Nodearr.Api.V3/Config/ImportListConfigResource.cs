using Nodearr.Http.REST;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;

namespace Nodearr.Api.V3.Config
{
    public class ImportListConfigResource : RestResource
    {
        public ListSyncLevelType ListSyncLevel { get; set; }
        public int ListSyncTag { get; set; }
    }

    public static class ImportListConfigResourceMapper
    {
        public static ImportListConfigResource ToResource(IConfigService model)
        {
            return new ImportListConfigResource
            {
                ListSyncLevel = model.ListSyncLevel,
                ListSyncTag = model.ListSyncTag,
            };
        }
    }
}

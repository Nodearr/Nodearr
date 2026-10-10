using NzbDrone.Core.Catalog;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Applications
{
    public interface IApplication : IProvider
    {
        // Makes sure the Arr app has Nodearr as a download client and as an indexer pinned to it.
        void SyncRegistration(bool force);

        ApplicationCatalog FetchCatalog();
    }
}

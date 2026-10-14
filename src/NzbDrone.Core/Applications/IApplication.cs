using NzbDrone.Core.Catalog;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Applications
{
    // Whether an Arr app can see a folder on Nodearr's side, and where it looked.
    public class FolderVisibility
    {
        public bool Found { get; set; }

        // The folder as the Arr app would address it, after its own remote path mappings.
        public string LookedAt { get; set; }
    }

    public interface IApplication : IProvider
    {
        // Makes sure the Arr app has Nodearr as a download client and as an indexer pinned to it.
        void SyncRegistration(bool force);

        // Takes Nodearr back out of the Arr app: the indexer, and the download client it is pinned to.
        void RemoveRegistration();

        ApplicationCatalog FetchCatalog();

        // One series, with its episodes and files, or one movie with its file, as the Arr app has
        // it now. Empty when the Arr app no longer has it.
        ApplicationCatalog FetchTitle(int arrId);

        // Asks the Arr app whether it can see a marker folder that was just made inside one of
        // Nodearr's folders. That is the only way to know the two really share the folder.
        FolderVisibility LookForFolder(string folder, string marker);
    }
}

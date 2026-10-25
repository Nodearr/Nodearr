using System.Linq;

namespace NzbDrone.Core.Catalog
{
    // How a file's revision is shown beside its quality, in the words the Arr apps use for it.
    // Nothing for a first release.
    public static class RevisionLabel
    {
        public static string For(int version, int real, bool isRepack)
        {
            var words = Enumerable.Repeat("Real", real).ToList();

            if (version > 1)
            {
                words.Add(isRepack ? (version == 2 ? "Repack" : $"Repack {version - 1}") : "Proper");
            }

            return words.Count > 0 ? string.Join(" ", words) : null;
        }
    }
}

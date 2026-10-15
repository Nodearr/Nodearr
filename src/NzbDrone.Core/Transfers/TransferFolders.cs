using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Nodes.Settings;

namespace NzbDrone.Core.Transfers
{
    public interface ITransferFolders
    {
        // Where verified transfers end up. This is the folder the Arr apps are told about.
        string Complete { get; }

        // The folder a transfer arrives in. It is removed or renamed, never left half done under Complete.
        string IncompleteFolder(TransferJob job);
        string IncompleteFile(TransferJob job);
        string CompleteFolder(TransferJob job);
    }

    // A transfer is written under incomplete/, and only a verified one is moved under complete/,
    // as a whole folder in one step. Both are on the same volume so that the move is a rename.
    public class TransferFolders : ITransferFolders
    {
        private const string DefaultExtension = ".mkv";

        private static readonly Regex ExtensionRegex = new(@"^\.[A-Za-z0-9]{1,5}$", RegexOptions.Compiled);

        // Not allowed in a file name on Windows. Left out everywhere, because the Arr app that
        // imports the file may be on another system than the one Nodearr runs on.
        private static readonly char[] UnsafeCharacters = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        private readonly INodeSettingsProvider _settings;

        public TransferFolders(INodeSettingsProvider settings)
        {
            _settings = settings;
        }

        // Asked for each time, because the folder is a setting that can change.
        private string Root => _settings.DownloadFolder;

        public string Complete => Path.Combine(Root, "complete");

        public string IncompleteFolder(TransferJob job)
        {
            return Path.Combine(Root, "incomplete", SafeName(job.DownloadId));
        }

        public string IncompleteFile(TransferJob job)
        {
            var extension = job.Extension != null && ExtensionRegex.IsMatch(job.Extension) ? job.Extension : DefaultExtension;

            return Path.Combine(IncompleteFolder(job), SafeName(job.Title) + extension);
        }

        // The same shape SABnzbd uses: one folder for each download, under the category's folder.
        public string CompleteFolder(TransferJob job)
        {
            return Path.Combine(Complete, SafeName(job.Category.IsNullOrWhiteSpace() ? "default" : job.Category), SafeName(job.Title));
        }

        private static string SafeName(string name)
        {
            var cleaned = new string((name ?? string.Empty).Where(c => !char.IsControl(c) && !UnsafeCharacters.Contains(c)).ToArray()).Trim(' ', '.');

            return cleaned.Length == 0 ? "transfer" : cleaned;
        }
    }
}

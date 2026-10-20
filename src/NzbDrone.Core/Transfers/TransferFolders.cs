using System;
using System.Collections.Generic;
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

        // The folder a category's transfers end up in, and making it before the first one does:
        // the Arr apps check that the folder they are told about exists, and warn when it does not.
        string CategoryFolder(string category);
        void EnsureCategoryFolders(IEnumerable<string> categories);
    }

    // A transfer is written under incomplete/, and only a verified one is moved under complete/,
    // as a whole folder in one step. Both are on the same volume so that the move is a rename.
    public class TransferFolders : ITransferFolders
    {
        private const string DefaultExtension = ".mkv";

        // Long enough for any real title, short enough for Windows' 260-character paths once the
        // folders before a file are counted.
        private const int MaxFileNameLength = 150;

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

            // An album's tracks are named by the release and numbered in the order Lidarr listed
            // them. Lidarr reads the rest from their tags. A Lidarr that does not rename keeps these
            // names, all in the artist's folder, so each album's have to be its own.
            if (job.HasParts)
            {
                var parts = job.GetParts();
                var name = parts[job.PartIndex].Name;

                // A track whose title the peer did not say is named by the release and its place.
                if (name == null)
                {
                    var width = parts.Count >= 100 ? 3 : 2;

                    name = $"{job.Title} - {(job.PartIndex + 1).ToString().PadLeft(width, '0')}";
                }

                return Path.Combine(IncompleteFolder(job), Shorten(SafeName(name)) + extension);
            }

            return Path.Combine(IncompleteFolder(job), SafeName(job.Title) + extension);
        }

        // The same shape SABnzbd uses: one folder for each download, under the category's folder.
        public string CompleteFolder(TransferJob job)
        {
            return Path.Combine(CategoryFolder(job.Category), SafeName(job.Title));
        }

        public string CategoryFolder(string category)
        {
            return Path.Combine(Complete, SafeName(category.IsNullOrWhiteSpace() ? "default" : category));
        }

        // A folder that cannot be made is left for later: the transfer that needs it makes it again,
        // and System > Status already warns when an app cannot see the download folder.
        public void EnsureCategoryFolders(IEnumerable<string> categories)
        {
            foreach (var category in categories)
            {
                try
                {
                    Directory.CreateDirectory(CategoryFolder(category));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        private static string Shorten(string name)
        {
            return name.Length <= MaxFileNameLength ? name : name[..MaxFileNameLength].TrimEnd(' ', '.');
        }

        private static string SafeName(string name)
        {
            var cleaned = new string((name ?? string.Empty).Where(c => !char.IsControl(c) && !UnsafeCharacters.Contains(c)).ToArray()).Trim(' ', '.');

            return cleaned.Length == 0 ? "transfer" : cleaned;
        }
    }
}

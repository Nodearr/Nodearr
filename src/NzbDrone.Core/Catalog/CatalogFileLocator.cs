using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Applications;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Catalog
{
    public class LocatedFile
    {
        // The Arr app's path, taken through the path mappings. It may still not be a path here.
        public string LocalPath { get; set; }

        // False for an unmapped path from an Arr app on another OS, which is no path here at all.
        public bool IsPathHere { get; set; }

        // There is a file at the path. Whether Nodearr may open it is Open's to say.
        public bool Exists { get; set; }
    }

    public enum CatalogFileAccess
    {
        Readable,

        // No file at the path, or no path here at all.
        Missing,

        // The file is there, and Nodearr may not open it or search its folder.
        NotAllowed
    }

    public interface ICatalogFileLocator
    {
        // Where a file an Arr app reported is on this machine.
        LocatedFile Locate(int applicationId, string arrPath);

        // The same, for a file named the way offers name it: e-{application}-{file},
        // m-{application}-{file}, or an album's track, t-{application}-{file}. Null when there is
        // no such file in the catalog.
        LocatedFile Locate(string offerFileId);

        // The same for many files at once, with a few queries in all. A name with no file in the
        // catalog is left out.
        Dictionary<string, LocatedFile> Locate(IEnumerable<string> offerFileIds);

        // Opens a located file and closes it again, to tell a file that is not there from one
        // Nodearr may not read. It touches the disk once or twice, so it is for a page of files
        // or a sample, not a whole library.
        CatalogFileAccess Open(LocatedFile file);
    }

    // Path mappings are keyed by the Arr app's host, the same way the Arr apps key theirs by
    // download client host.
    public class CatalogFileLocator : ICatalogFileLocator
    {
        private readonly IApplicationFactory _applicationFactory;
        private readonly IRemotePathMappingService _remotePathMappingService;
        private readonly ICatalogEpisodeFileRepository _episodeFiles;
        private readonly ICatalogMovieFileRepository _movieFiles;
        private readonly ICatalogTrackFileRepository _trackFiles;
        private readonly IDiskProvider _diskProvider;

        public CatalogFileLocator(IApplicationFactory applicationFactory,
                                  IRemotePathMappingService remotePathMappingService,
                                  ICatalogEpisodeFileRepository episodeFiles,
                                  ICatalogMovieFileRepository movieFiles,
                                  ICatalogTrackFileRepository trackFiles,
                                  IDiskProvider diskProvider)
        {
            _applicationFactory = applicationFactory;
            _remotePathMappingService = remotePathMappingService;
            _episodeFiles = episodeFiles;
            _movieFiles = movieFiles;
            _trackFiles = trackFiles;
            _diskProvider = diskProvider;
        }

        public LocatedFile Locate(int applicationId, string arrPath)
        {
            return Locate(_applicationFactory.All().FirstOrDefault(a => a.Id == applicationId), arrPath);
        }

        public LocatedFile Locate(string offerFileId)
        {
            if (!TryParse(offerFileId, out var kind, out var applicationId, out var arrId))
            {
                return null;
            }

            CatalogFile file = kind switch
            {
                "e" => _episodeFiles.ForArrIds(applicationId, new[] { arrId }).FirstOrDefault(f => f.MissingSince == null),
                "t" => _trackFiles.ForArrIds(applicationId, new[] { arrId }).FirstOrDefault(f => f.MissingSince == null),
                _ => _movieFiles.ForArrIds(applicationId, new[] { arrId }).FirstOrDefault(f => f.MissingSince == null)
            };

            return file == null ? null : Locate(applicationId, file.Path);
        }

        public Dictionary<string, LocatedFile> Locate(IEnumerable<string> offerFileIds)
        {
            var applications = _applicationFactory.All().ToDictionary(a => a.Id);
            var located = new Dictionary<string, LocatedFile>();
            var wanted = offerFileIds
                .Select(id => TryParse(id, out var kind, out var applicationId, out var arrId) ? (id, kind, applicationId, arrId) : default)
                .Where(w => w.id != null)
                .GroupBy(w => (w.kind, w.applicationId));

            foreach (var group in wanted)
            {
                var (kind, applicationId) = group.Key;
                var files = Files(kind, applicationId)
                    .Where(f => f.MissingSince == null)
                    .ToDictionary(f => f.ArrId);

                foreach (var (id, _, _, arrId) in group)
                {
                    if (files.TryGetValue(arrId, out var file))
                    {
                        located[id] = Locate(applications.GetValueOrDefault(applicationId), file.Path);
                    }
                }
            }

            return located;
        }

        private IEnumerable<CatalogFile> Files(string kind, int applicationId)
        {
            return kind switch
            {
                "e" => _episodeFiles.ForApplication(applicationId),
                "t" => _trackFiles.ForApplication(applicationId),
                _ => _movieFiles.ForApplication(applicationId)
            };
        }

        public CatalogFileAccess Open(LocatedFile file)
        {
            if (file is not { IsPathHere: true })
            {
                return CatalogFileAccess.Missing;
            }

            try
            {
                using (_diskProvider.OpenReadStream(file.LocalPath))
                {
                    return CatalogFileAccess.Readable;
                }
            }
            catch (FileNotFoundException)
            {
                // Said too when the file is there and its folder may not be searched, since that
                // hides it from the check for whether it exists. The folder's listing tells which.
                return InFolderListing(file.LocalPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ex is DirectoryNotFoundException ? CatalogFileAccess.Missing : CatalogFileAccess.NotAllowed;
            }
        }

        private CatalogFileAccess InFolderListing(string path)
        {
            try
            {
                var listed = _diskProvider.GetFileInfos(Path.GetDirectoryName(path))
                    .Any(f => string.Equals(f.FullName, path, OsInfo.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

                return listed ? CatalogFileAccess.NotAllowed : CatalogFileAccess.Missing;
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or ArgumentException)
            {
                return CatalogFileAccess.Missing;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return CatalogFileAccess.NotAllowed;
            }
        }

        private static bool TryParse(string offerFileId, out string kind, out int applicationId, out int arrId)
        {
            var parts = (offerFileId ?? string.Empty).Split('-');

            kind = parts[0];
            applicationId = 0;
            arrId = 0;

            return parts.Length == 3 && kind is "e" or "m" or "t" && int.TryParse(parts[1], out applicationId) && int.TryParse(parts[2], out arrId);
        }

        private LocatedFile Locate(ApplicationDefinition application, string arrPath)
        {
            if (application == null || arrPath.IsNullOrWhiteSpace())
            {
                return new LocatedFile { LocalPath = arrPath };
            }

            var host = Uri.TryCreate((application.Settings as ArrApplicationSettings)?.BaseUrl, UriKind.Absolute, out var url) ? url.Host : string.Empty;
            var local = _remotePathMappingService.RemapRemoteToLocal(host, new OsPath(arrPath));

            // An unmapped path from an Arr app on another OS is not a path here at all.
            var isPathHere = local.IsRooted && (OsInfo.IsWindows ? local.IsWindowsPath : local.IsUnixPath);

            return new LocatedFile { LocalPath = local.FullPath, IsPathHere = isPathHere, Exists = isPathHere && _diskProvider.FileExists(local.FullPath) };
        }
    }
}

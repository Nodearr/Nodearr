using System;
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
        public bool Readable { get; set; }
    }

    public interface ICatalogFileLocator
    {
        // Where a file an Arr app reported is on this machine.
        LocatedFile Locate(int applicationId, string arrPath);

        // The same, for a file named the way offers name it: e-{application}-{file} or
        // m-{application}-{file}. Null when there is no such file in the catalog.
        LocatedFile Locate(string offerFileId);
    }

    // Path mappings are keyed by the Arr app's host, the same way the Arr apps key theirs by
    // download client host.
    public class CatalogFileLocator : ICatalogFileLocator
    {
        private readonly IApplicationFactory _applicationFactory;
        private readonly IRemotePathMappingService _remotePathMappingService;
        private readonly ICatalogEpisodeFileRepository _episodeFiles;
        private readonly ICatalogMovieFileRepository _movieFiles;
        private readonly IDiskProvider _diskProvider;

        public CatalogFileLocator(IApplicationFactory applicationFactory,
                                  IRemotePathMappingService remotePathMappingService,
                                  ICatalogEpisodeFileRepository episodeFiles,
                                  ICatalogMovieFileRepository movieFiles,
                                  IDiskProvider diskProvider)
        {
            _applicationFactory = applicationFactory;
            _remotePathMappingService = remotePathMappingService;
            _episodeFiles = episodeFiles;
            _movieFiles = movieFiles;
            _diskProvider = diskProvider;
        }

        public LocatedFile Locate(int applicationId, string arrPath)
        {
            var application = _applicationFactory.All().FirstOrDefault(a => a.Id == applicationId);

            if (application == null || arrPath.IsNullOrWhiteSpace())
            {
                return new LocatedFile { LocalPath = arrPath };
            }

            var host = Uri.TryCreate((application.Settings as ArrApplicationSettings)?.BaseUrl, UriKind.Absolute, out var url) ? url.Host : string.Empty;
            var local = _remotePathMappingService.RemapRemoteToLocal(host, new OsPath(arrPath));

            // An unmapped path from an Arr app on another OS is not a path here at all.
            var isPathHere = local.IsRooted && (OsInfo.IsWindows ? local.IsWindowsPath : local.IsUnixPath);

            return new LocatedFile { LocalPath = local.FullPath, Readable = isPathHere && _diskProvider.FileExists(local.FullPath) };
        }

        public LocatedFile Locate(string offerFileId)
        {
            var parts = (offerFileId ?? string.Empty).Split('-');

            if (parts.Length != 3 || !int.TryParse(parts[1], out var applicationId) || !int.TryParse(parts[2], out var arrId))
            {
                return null;
            }

            CatalogFile file = parts[0] switch
            {
                "e" => _episodeFiles.ForArrIds(applicationId, new[] { arrId }).FirstOrDefault(f => f.MissingSince == null),
                "m" => _movieFiles.ForArrIds(applicationId, new[] { arrId }).FirstOrDefault(f => f.MissingSince == null),
                _ => null
            };

            return file == null ? null : Locate(applicationId, file.Path);
        }
    }
}

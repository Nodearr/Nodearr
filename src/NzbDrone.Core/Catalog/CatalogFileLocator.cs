using System;
using System.Collections.Generic;
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

        // The same for many files at once, with a few queries in all. A name with no file in the
        // catalog is left out.
        Dictionary<string, LocatedFile> Locate(IEnumerable<string> offerFileIds);
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
                var files = (kind == "e" ? _episodeFiles.ForApplication(applicationId).Cast<CatalogFile>() : _movieFiles.ForApplication(applicationId))
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

        private static bool TryParse(string offerFileId, out string kind, out int applicationId, out int arrId)
        {
            var parts = (offerFileId ?? string.Empty).Split('-');

            kind = parts[0];
            applicationId = 0;
            arrId = 0;

            return parts.Length == 3 && kind is "e" or "m" && int.TryParse(parts[1], out applicationId) && int.TryParse(parts[2], out arrId);
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

            return new LocatedFile { LocalPath = local.FullPath, Readable = isPathHere && _diskProvider.FileExists(local.FullPath) };
        }
    }
}

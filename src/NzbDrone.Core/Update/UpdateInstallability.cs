using System;
using System.IO;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Update
{
    // How this install can take a new version (ADR 0024).
    public enum UpdateInstallKind
    {
        // Nodearr.Update replaces the program folder: the Windows installer's, a zip or an archive.
        BuiltIn,

        // The owner's own update script.
        Script,

        // A container. Updating is pulling the new image.
        Docker,

        // A package manager's, which updates it.
        External,

        // Nodearr cannot change the folder it runs from, so it only says there is an update.
        NotWritable,

        // The manifest has no package for this platform.
        NoPackage,

        // Run from a development build, which is never replaced.
        Development
    }

    public interface IUpdateInstallability
    {
        UpdateInstallKind Kind(UpdatePackage package);
    }

    public class UpdateInstallability : IUpdateInstallability
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IDeploymentInfoProvider _deploymentInfoProvider;

        public UpdateInstallability(IAppFolderInfo appFolderInfo,
                                    IDiskProvider diskProvider,
                                    IConfigFileProvider configFileProvider,
                                    IDeploymentInfoProvider deploymentInfoProvider)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _configFileProvider = configFileProvider;
            _deploymentInfoProvider = deploymentInfoProvider;
        }

        public UpdateInstallKind Kind(UpdatePackage package)
        {
            // As in Sonarr, the image says it is one, in its package_info. An archive unpacked into
            // a container of someone's own updates itself like any other.
            if (_deploymentInfoProvider.PackageUpdateMechanism == UpdateMechanism.Docker || _configFileProvider.UpdateMechanism == UpdateMechanism.Docker)
            {
                return UpdateInstallKind.Docker;
            }

            if (_deploymentInfoProvider.IsExternalUpdateMechanism)
            {
                return UpdateInstallKind.External;
            }

            if (_configFileProvider.UpdateMechanism == UpdateMechanism.Script)
            {
                return package?.Url == null ? UpdateInstallKind.NoPackage : UpdateInstallKind.Script;
            }

            var startupFolder = _appFolderInfo.StartUpFolder;

            // A build runs from _output, or a folder in it such as _output/net10.0.
            if (startupFolder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                             .Any(part => part.Equals("_output", StringComparison.OrdinalIgnoreCase)))
            {
                return UpdateInstallKind.Development;
            }

            if (package != null && package.Url == null)
            {
                return UpdateInstallKind.NoPackage;
            }

            if (!_diskProvider.FolderWritable(startupFolder) || !_diskProvider.FolderWritable(Path.Combine(startupFolder, "UI")))
            {
                return UpdateInstallKind.NotWritable;
            }

            return UpdateInstallKind.BuiltIn;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Nodes.Settings
{
    public interface INodeSettingsProvider
    {
        // The settings in force: what the environment fixes, then what was saved, then the defaults.
        NodeSettings Get();

        // The names of the settings the environment fixes. Those cannot be changed from the UI.
        ISet<string> FixedByEnvironment();

        // The node's name as other nodes see it, never empty.
        string NodeName { get; }

        // The download folder in force, never empty.
        string DownloadFolder { get; }
        string DefaultDownloadFolder { get; }
    }

    // A setting given in the environment (Nodearr__Transport__...) always wins. That is how a
    // container is configured, and it keeps what the container was built around, such as its
    // /downloads mount and its published port, from being changed out from under it.
    public class NodeSettingsProvider : INodeSettingsProvider
    {
        private readonly IConfigService _configService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly TransportOptions _options;

        public NodeSettingsProvider(IConfigService configService, IAppFolderInfo appFolderInfo, IOptions<TransportOptions> options)
        {
            _configService = configService;
            _appFolderInfo = appFolderInfo;
            _options = options?.Value ?? new TransportOptions();
        }

        public string DefaultDownloadFolder => Path.Combine(_appFolderInfo.AppDataFolder, "downloads");

        public string NodeName
        {
            get
            {
                var name = Get().NodeName;

                return name.IsNotNullOrWhiteSpace() ? name.Trim() : Environment.MachineName;
            }
        }

        public string DownloadFolder
        {
            get
            {
                var folder = Get().DownloadFolder;

                return folder.IsNotNullOrWhiteSpace() ? Path.GetFullPath(folder) : DefaultDownloadFolder;
            }
        }

        public NodeSettings Get()
        {
            return new NodeSettings
            {
                NodeName = _options.NodeName ?? _configService.NodeName,
                Relay = _options.Relay.IsNotNullOrWhiteSpace() ? ParseRelay(_options.Relay) : ParseRelay(_configService.TransportRelay),
                RelayUrl = _options.RelayUrl ?? _configService.TransportRelayUrl,
                Port = _options.Port ?? _configService.TransportPort,
                PortMapping = _options.DisablePortMapping.HasValue ? !_options.DisablePortMapping.Value : _configService.TransportPortMapping,
                DirectTimeoutSeconds = _options.DirectTimeoutSeconds ?? _configService.TransferDirectTimeout,
                DownloadFolder = _options.DownloadFolder ?? _configService.TransferDownloadFolder,
                UploadLimitKbps = _options.UploadLimitKbps ?? _configService.TransferUploadLimit,

                // Not something a container is built around, so the environment does not set it.
                CatalogSyncInterval = _configService.CatalogSyncInterval
            };
        }

        public ISet<string> FixedByEnvironment()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(bool isSet, string name)
            {
                if (isSet)
                {
                    names.Add(name);
                }
            }

            Add(_options.NodeName != null, nameof(NodeSettings.NodeName));
            Add(_options.Relay.IsNotNullOrWhiteSpace(), nameof(NodeSettings.Relay));
            Add(_options.RelayUrl != null, nameof(NodeSettings.RelayUrl));
            Add(_options.Port.HasValue, nameof(NodeSettings.Port));
            Add(_options.DisablePortMapping.HasValue, nameof(NodeSettings.PortMapping));
            Add(_options.DirectTimeoutSeconds.HasValue, nameof(NodeSettings.DirectTimeoutSeconds));
            Add(_options.DownloadFolder != null, nameof(NodeSettings.DownloadFolder));
            Add(_options.UploadLimitKbps.HasValue, nameof(NodeSettings.UploadLimitKbps));

            return names;
        }

        // Anything unrecognised is the default, so a typing mistake in the environment cannot
        // quietly switch relays off.
        private static RelayMode ParseRelay(string value)
        {
            return Enum.TryParse<RelayMode>(value, true, out var mode) ? mode : RelayMode.N0;
        }
    }
}

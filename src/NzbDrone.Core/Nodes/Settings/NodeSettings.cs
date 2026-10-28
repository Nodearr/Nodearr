using System.Collections.Generic;

namespace NzbDrone.Core.Nodes.Settings
{
    public enum RelayMode
    {
        // The public relays run by the makers of iroh. They carry coordination only (ADR 0011).
        N0 = 0,

        // A relay the owner runs, at RelayUrl.
        Custom = 1,

        // No relay. Nodes must already know an address each other can reach.
        Disabled = 2
    }

    // How this node presents itself, reaches other nodes, and stores what it fetches.
    public class NodeSettings
    {
        // What other nodes see this node called. The machine name when empty.
        public string NodeName { get; set; }
        public RelayMode Relay { get; set; }
        public string RelayUrl { get; set; }

        // The UDP port other nodes connect to. Zero lets the system pick one.
        public int Port { get; set; }

        // Ask the router to open the port (UPnP, NAT-PMP, PCP).
        public bool PortMapping { get; set; }

        // How long a transfer waits for a direct path before it gives up.
        public int DirectTimeoutSeconds { get; set; }

        // Where transfers are written. Empty means a downloads folder in the data folder.
        public string DownloadFolder { get; set; }

        // The most each upload may send, in kilobytes per second. Zero is no limit.
        public int UploadLimitKbps { get; set; }

        // The most all uploads together may send, in kilobytes per second. Zero is no limit.
        public int TotalUploadLimitKbps { get; set; }

        // The most each download may take, in kilobytes per second. Zero is no limit.
        public int DownloadLimitKbps { get; set; }

        // The most downloads that run at once. The rest wait their turn, queued. Zero is no limit.
        public int MaxDownloads { get; set; }

        // Minutes between asking each trusted node what changed in its catalog, like Sonarr's RSS
        // Sync Interval: 10 to 120, or 0 to ask only when told or asked by hand.
        public int CatalogSyncInterval { get; set; }

        public NodeSettings Clone()
        {
            return (NodeSettings)MemberwiseClone();
        }
    }

    public static class NodeSettingsDefaults
    {
        public const int DirectTimeoutSeconds = 20;

        // What Nodearr ran at once before it could be set (ADR 0016).
        public const int MaxDownloads = 3;
        public const int MaxMaxDownloads = 100;

        // Sonarr's default for its RSS Sync Interval, and its limits.
        public const int CatalogSyncInterval = 15;
        public const int MinCatalogSyncInterval = 10;
        public const int MaxCatalogSyncInterval = 120;
    }

    public class NodeSettingsSaved
    {
        public NodeSettings Settings { get; set; }

        // The peer transport had to be restarted to take the new settings.
        public bool TransportRestarted { get; set; }

        // After a restart: whether it came back. False means the settings were saved and the
        // transport would not start with them.
        public bool TransportRunning { get; set; }

        // Settings the request tried to change that are fixed by the environment, and were left alone.
        public List<string> Ignored { get; set; } = new();
    }
}

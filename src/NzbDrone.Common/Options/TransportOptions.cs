namespace NzbDrone.Common.Options;

public class TransportOptions
{
    // Path to the nodearr-transport binary. Defaults to the one next to the application.
    public string BinaryPath { get; set; }

    // n0 (default), custom or disabled.
    public string Relay { get; set; }
    public string RelayUrl { get; set; }

    // The UDP port other nodes connect to. A free one is picked when not set. Set it when the
    // port has to be published or forwarded, as for a container on a bridge network.
    public int? Port { get; set; }

    // What other nodes see this node called. The machine name when not set.
    public string NodeName { get; set; }

    // Port mapping (UPnP, NAT-PMP, PCP) is on unless this is set.
    public bool? DisablePortMapping { get; set; }

    // How long a transfer waits for a direct path before it fails. 20 seconds when not set.
    public int? DirectTimeoutSeconds { get; set; }

    // The most each upload may send, in kilobytes per second. No limit when not set.
    public int? UploadLimitKbps { get; set; }

    // The most all uploads together may send, in kilobytes per second. No limit when not set.
    public int? TotalUploadLimitKbps { get; set; }

    // The most each download may take, in kilobytes per second. No limit when not set.
    public int? DownloadLimitKbps { get; set; }

    // The most downloads from other nodes that run at once. Three when not set, 0 for no limit.
    public int? MaxDownloads { get; set; }

    // Where transfers are written: incomplete/ while they arrive, complete/ once verified.
    // Defaults to a downloads folder in the data folder.
    public string DownloadFolder { get; set; }
}

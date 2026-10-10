namespace NzbDrone.Common.Options;

public class TransportOptions
{
    // Path to the nodearr-transport binary. Defaults to the one next to the application.
    public string BinaryPath { get; set; }

    // n0 (default), custom or disabled.
    public string Relay { get; set; }
    public string RelayUrl { get; set; }

    // Port mapping (UPnP, NAT-PMP, PCP) is on unless this is set.
    public bool? DisablePortMapping { get; set; }

    // How long a transfer waits for a direct path before it fails. 20 seconds when not set.
    public int? DirectTimeoutSeconds { get; set; }

    // The most each upload may send, in kilobytes per second. No limit when not set.
    public int? UploadLimitKbps { get; set; }

    // Where transfers are written: incomplete/ while they arrive, complete/ once verified.
    // Defaults to a downloads folder in the data folder.
    public string DownloadFolder { get; set; }
}

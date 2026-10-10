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
}

namespace NzbDrone.Common.Options;

public class NetworkOptions
{
    // Nodearr__Network__ContactNodearr=false stops Nodearr contacting updates.nodearr.com and
    // usage.nodearr.com, whatever their own settings say (ADR 0024).
    public bool? ContactNodearr { get; set; }
}

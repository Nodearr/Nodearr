namespace NzbDrone.Common.Options;

public class UsageOptions
{
    // Nodearr__Usage__Send: whether to send the anonymous usage report to usage.nodearr.com.
    public bool? Send { get; set; }

    // For testing, and only in the environment: where the report is posted, in place of
    // https://usage.nodearr.com/v1/report. Whoever sets the environment can replace the program anyway.
    public string Url { get; set; }
}

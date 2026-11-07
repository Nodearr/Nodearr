using System.Collections.Generic;

namespace NzbDrone.Core.Usage
{
    // The anonymous usage report, as sent to usage.nodearr.com (ADR 0024). Every field is listed in
    // usage/schema/v1/report.schema.json and in "What anyone else sees" in the user guide's privacy
    // page; a field added here is added to both in the same change. Nothing in it names this node,
    // its owner, a friend, a title, a path or an address, and every count is a range.
    public class UsageReport
    {
        public const int CurrentSchema = 1;

        public int Schema { get; set; } = CurrentSchema;

        // Random, and new each day at midnight UTC, so that a day's reports from one node can be
        // counted once and nothing links one day to the next.
        public string Id { get; set; }

        public UsageNodearr Nodearr { get; set; } = new();

        // Moved a file, or answered a grab, in the past 30 days.
        public bool Active { get; set; }

        public List<UsageApp> Apps { get; set; } = new();

        // Trusted friends.
        public string Friends { get; set; }

        // Titles shared with at least one friend, by kind of Arr app: sonarr, radarr, lidarr,
        // whisparr. A kind with no app connected is left out.
        public SortedDictionary<string, string> SharedTitles { get; set; } = new();

        public UsagePastWeek Past7Days { get; set; } = new();
        public UsageNetwork Network { get; set; } = new();
        public UsageFeatures Features { get; set; } = new();

        // The language the UI is shown in, such as en or pt-BR.
        public string Language { get; set; }
    }

    public class UsageNodearr
    {
        public string Version { get; set; }
        public string Branch { get; set; }

        // The runtime identifier the package was built for: win-x64, linux-musl-arm64, osx-arm64.
        public string Platform { get; set; }

        // The .NET runtime version.
        public string Runtime { get; set; }

        // sqlite or postgres.
        public string Database { get; set; }

        // installerTray, installerService, zip, tarball, docker, package or development.
        public string Install { get; set; }
    }

    public class UsageApp
    {
        // sonarr, radarr, lidarr, whisparr, prowlarr.
        public string Kind { get; set; }

        // Major and minor only, such as 4.0, or unknown when the app didn't answer.
        public string Version { get; set; }
    }

    public class UsagePastWeek
    {
        public UsageDirection Sent { get; set; } = new();
        public UsageDirection Fetched { get; set; } = new();

        // Titles sent to a friend with Send.
        public string Sends { get; set; }
    }

    public class UsageDirection
    {
        public string Finished { get; set; }
        public string Failed { get; set; }

        // How much data moved, finished or not.
        public string Data { get; set; }

        // Failed transfers by reason, such as NoDirectRoute. A reason with none is left out.
        public SortedDictionary<string, string> Failures { get; set; } = new();
    }

    public class UsageNetwork
    {
        // Trusted friends by what connection checks in the past 7 days found: a direct route at
        // least once, only a relay, or no way at all.
        public string FriendsDirect { get; set; }
        public string FriendsRelayOnly { get; set; }
        public string FriendsUnreachable { get; set; }

        // Whether this node's network gives a different public port for each destination, and
        // whether UDP gets out. Null when the transport doesn't know.
        public bool? PortVaries { get; set; }
        public bool? Udp { get; set; }

        // n0 for the iroh team's relays, own for the owner's, off for none.
        public string Relay { get; set; }

        // Running in a container on Docker Desktop.
        public bool DockerDesktop { get; set; }
    }

    public class UsageFeatures
    {
        // A share list with at least one title and one friend.
        public bool ShareLists { get; set; }

        // Titles on the Hidden list.
        public bool HiddenTitles { get; set; }

        // At least one friend is offered everything.
        public bool ShareEverything { get; set; }

        // At least one Arr app fetches friends' new files automatically.
        public bool FetchAutomatically { get; set; }

        public bool UploadLimit { get; set; }
        public bool TotalUploadLimit { get; set; }
        public bool DownloadLimit { get; set; }

        // Most Uploads at Once: default, other, or none for no limit.
        public string MaxUploads { get; set; }
    }
}

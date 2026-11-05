using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Statistics
{
    public enum TransferDirection
    {
        // From a friend to this node.
        Fetched = 0,

        // From this node to a friend.
        Sent = 1
    }

    // What moved in one hour, UTC, with one friend, for one Arr app, one way, and how it ended.
    // Rows only ever add up, so the page can sum any range of them, and a day in any time zone is
    // whole hours of them. Kept 13 months (TrimStatistics).
    public class TransferStatistic : ModelBase
    {
        // The start of the hour, UTC.
        public DateTime Hour { get; set; }
        public string PeerNodeId { get; set; }

        // Zero for a file no Arr app could be found for.
        public int ApplicationId { get; set; }
        public TransferDirection Direction { get; set; }

        // Empty for transfers that finished. Otherwise why they failed: a TransferFailure for what
        // was fetched, the transport's word for what was sent.
        public string Failure { get; set; }

        // Bytes that moved, not counting what the other side already held when an attempt began.
        public long Bytes { get; set; }

        // Transfers that ended this way in this hour. Only ever goes up, so it can be read as a
        // running total (/metrics).
        public int Transfers { get; set; }

        // Of those that failed, how many the other node asked for again afterwards, so that the
        // failure did not end the transfer after all. The page leaves them out (Counted).
        public int Reopened { get; set; }

        // How long the data was moving. Connecting and waiting do not count.
        public double Seconds { get; set; }

        // The fastest finished transfer's own average, in bytes per second.
        public double PeakBytesPerSecond { get; set; }

        // Sent only: how many requests arrived, how many of them had to wait for a free upload
        // slot, for how long in all, and the longest wait.
        public int Requests { get; set; }
        public int Waited { get; set; }
        public double WaitSeconds { get; set; }
        public double LongestWaitSeconds { get; set; }

        public bool IsFinished => string.IsNullOrEmpty(Failure);

        // Transfers that ended this way for good.
        public int Counted => Math.Max(0, Transfers - Reopened);
    }

    // What the connection checks to one friend found in one hour, UTC.
    public class ConnectionStatistic : ModelBase
    {
        public DateTime Hour { get; set; }
        public string PeerNodeId { get; set; }
        public int Direct { get; set; }
        public int RelayOnly { get; set; }

        // The check found no way to the friend.
        public int None { get; set; }

        // How often the route found differed from the one found before.
        public int RouteChanges { get; set; }
    }

    // The last route a check found to a friend, direct or relay only, and when it last changed.
    // A check that finds no way at all leaves it as it was: a friend switching off is not a new
    // route.
    public class PeerRoute : ModelBase
    {
        public string PeerNodeId { get; set; }
        public ConnectionPath Path { get; set; }
        public DateTime? Changed { get; set; }
    }

    // A title a friend fetched from this node in one hour, UTC: a series, a movie or an album, by
    // the key StatisticsTitles gives it. Kept so the page can count different titles in a range.
    public class FetchedTitle : ModelBase
    {
        public DateTime Hour { get; set; }
        public int ApplicationId { get; set; }
        public string Title { get; set; }
    }

    public static class StatisticsHours
    {
        // How long the hourly rows are kept.
        public const int MonthsKept = 13;

        // Where the hours older than that are added up once they are trimmed, one row for each
        // friend, Arr app, direction and ending, so the totals since the start never go down
        // (/metrics). No range the page asks for reaches back this far.
        public static readonly DateTime Carried = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime HourOf(DateTime time)
        {
            var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time;

            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        }
    }
}

using System.Linq;

namespace NzbDrone.Core.Usage
{
    // The ranges the usage report gives in place of counts, so that no exact number can single a
    // node out (ADR 0024). The names are the schema's (usage/schema/v1/report.schema.json).
    public static class UsageBuckets
    {
        public const long GB = 1_000_000_000L;
        public const long TB = 1000 * GB;

        // Trusted friends: 0, 1, 2-5, 6-10, 11+.
        public static readonly long[] FriendBounds = { 0, 1, 5, 10 };

        // Transfers, failures and sends: 0, 1-5, 6-25, 26-100, 101-500, 501+.
        public static readonly long[] CountBounds = { 0, 5, 25, 100, 500 };

        // Titles: 0, 1-10, 11-100, 101-1000, 1001-10000, 10001+.
        public static readonly long[] TitleBounds = { 0, 10, 100, 1000, 10000 };

        public static string Friends(long count) => Range(count, FriendBounds);
        public static string Count(long count) => Range(count, CountBounds);
        public static string Titles(long count) => Range(count, TitleBounds);

        // Data moved: 0, <1GB, 1-10GB, 10-100GB, 100GB-1TB, >1TB. A bound belongs to the range
        // below it: exactly 1 GB is <1GB.
        public static string Data(long bytes)
        {
            return bytes switch
            {
                <= 0 => "0",
                <= GB => "<1GB",
                <= 10 * GB => "1-10GB",
                <= 100 * GB => "10-100GB",
                <= TB => "100GB-1TB",
                _ => ">1TB"
            };
        }

        // Each bound is the top of a range; the first range starts at zero, the next one above the
        // bound before it, and the last has no top.
        private static string Range(long count, long[] bounds)
        {
            if (count <= 0)
            {
                return "0";
            }

            for (var i = 1; i < bounds.Length; i++)
            {
                if (count <= bounds[i])
                {
                    var low = bounds[i - 1] + 1;

                    return low == bounds[i] ? low.ToString() : $"{low}-{bounds[i]}";
                }
            }

            return $"{bounds.Last() + 1}+";
        }

        public static string[] AllFriends() => All(FriendBounds);
        public static string[] AllCounts() => All(CountBounds);
        public static string[] AllTitles() => All(TitleBounds);
        public static string[] AllData() => new[] { "0", "<1GB", "1-10GB", "10-100GB", "100GB-1TB", ">1TB" };

        private static string[] All(long[] bounds)
        {
            return new long[] { 0 }
                .Concat(bounds.Skip(1))
                .Append(bounds.Last() + 1)
                .Select(n => Range(n, bounds))
                .ToArray();
        }
    }
}

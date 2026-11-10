using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Usage;

namespace NzbDrone.Core.Test.Usage
{
    [TestFixture]
    public class UsageBucketsFixture
    {
        [TestCase(0, "0")]
        [TestCase(1, "1")]
        [TestCase(2, "2-5")]
        [TestCase(5, "2-5")]
        [TestCase(6, "6-10")]
        [TestCase(10, "6-10")]
        [TestCase(11, "11+")]
        [TestCase(250, "11+")]
        public void friends_should_be_counted_in_the_ranges_the_issue_gives(long count, string expected)
        {
            UsageBuckets.Friends(count).Should().Be(expected);
        }

        [TestCase(-1, "0")]
        [TestCase(0, "0")]
        [TestCase(1, "1-5")]
        [TestCase(5, "1-5")]
        [TestCase(6, "6-25")]
        [TestCase(100, "26-100")]
        [TestCase(101, "101-500")]
        [TestCase(501, "501+")]
        public void transfers_should_be_counted_in_ranges(long count, string expected)
        {
            UsageBuckets.Count(count).Should().Be(expected);
        }

        [TestCase(0, "0")]
        [TestCase(1, "1-10")]
        [TestCase(1000, "101-1000")]
        [TestCase(1001, "1001-10000")]
        [TestCase(10001, "10001+")]
        public void titles_should_be_counted_in_ranges(long count, string expected)
        {
            UsageBuckets.Titles(count).Should().Be(expected);
        }

        [TestCase(0L, "0")]
        [TestCase(1L, "<1GB")]
        [TestCase(1_000_000_000L, "<1GB")]
        [TestCase(1_000_000_001L, "1-10GB")]
        [TestCase(99_000_000_000L, "10-100GB")]
        [TestCase(500_000_000_000L, "100GB-1TB")]
        [TestCase(1_000_000_000_001L, ">1TB")]
        public void data_should_be_given_in_ranges(long bytes, string expected)
        {
            UsageBuckets.Data(bytes).Should().Be(expected);
        }

        [Test]
        public void every_range_should_be_one_the_schema_lists()
        {
            var defs = ReportSchema.Load().GetProperty("$defs");

            Values(defs, "friends").Should().Equal(UsageBuckets.AllFriends());
            Values(defs, "count").Should().Equal(UsageBuckets.AllCounts());
            Values(defs, "titles").Should().Equal(UsageBuckets.AllTitles());
            Values(defs, "data").Should().Equal(UsageBuckets.AllData());
        }

        [Test]
        public void every_failure_reason_should_be_one_the_schema_lists()
        {
            var reasons = ReportSchema.Load().GetProperty("$defs").GetProperty("direction").GetProperty("properties").GetProperty("failures").GetProperty("propertyNames");

            Values(reasons).Should().BeEquivalentTo(UsageReportBuilder.FailureReasons);
        }

        private static string[] Values(System.Text.Json.JsonElement defs, string name) => Values(defs.GetProperty(name));

        private static string[] Values(System.Text.Json.JsonElement schema)
        {
            var values = new System.Collections.Generic.List<string>();

            foreach (var value in schema.GetProperty("enum").EnumerateArray())
            {
                values.Add(value.GetString());
            }

            return values.ToArray();
        }
    }
}

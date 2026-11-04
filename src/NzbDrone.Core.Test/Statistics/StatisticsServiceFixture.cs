using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Statistics
{
    [TestFixture]
    public class StatisticsServiceFixture : DbTest<StatisticsService, TransferStatistic>
    {
        private static readonly DateTime Day = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

        private int _maxUploads;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ITransferStatisticRepository>(Mocker.Resolve<TransferStatisticRepository>());
            Mocker.SetConstant<IConnectionStatisticRepository>(Mocker.Resolve<ConnectionStatisticRepository>());
            Mocker.SetConstant<IPeerRouteRepository>(Mocker.Resolve<PeerRouteRepository>());
            Mocker.SetConstant<IFetchedTitleRepository>(Mocker.Resolve<FetchedTitleRepository>());

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(new List<ApplicationDefinition>
            {
                new() { Id = 3, Name = "Sonarr" },
                new() { Id = 4, Name = "Radarr" }
            });
            Mocker.GetMock<IPeerRepository>().Setup(r => r.All()).Returns(new List<Peer>
            {
                new() { NodeId = "alice", Name = "Alice", State = PeerState.Trusted },
                new() { NodeId = "bob", Name = "Bob", State = PeerState.Trusted }
            });
            Mocker.GetMock<IStatisticsTitles>().Setup(t => t.SharedTitles()).Returns(new Dictionary<int, int> { [3] = 12, [4] = 40 });

            _maxUploads = 3;
            Mocker.GetMock<INodeSettingsProvider>().Setup(s => s.Get()).Returns(() => new NodeSettings { MaxUploads = _maxUploads });
        }

        private void GivenRow(DateTime hour, TransferDirection direction, long bytes, double seconds, string failure = "", int transfers = 1, double peak = 0, string peer = "alice", int applicationId = 3, int requests = 0, int waited = 0, double waitSeconds = 0)
        {
            Db.Insert(new TransferStatistic
            {
                Hour = hour,
                PeerNodeId = peer,
                ApplicationId = applicationId,
                Direction = direction,
                Failure = failure,
                Bytes = bytes,
                Transfers = transfers,
                Seconds = seconds,
                PeakBytesPerSecond = peak,
                Requests = requests,
                Waited = waited,
                WaitSeconds = waitSeconds,
                LongestWaitSeconds = waitSeconds
            });
        }

        [Test]
        public void only_hours_in_the_range_should_count()
        {
            GivenRow(Day.AddHours(-1), TransferDirection.Fetched, 100, 1);
            GivenRow(Day.AddHours(10), TransferDirection.Fetched, 1000, 1);
            GivenRow(Day.AddDays(1), TransferDirection.Fetched, 10000, 1);

            var report = Subject.Get(Day, Day.AddDays(1), "UTC");

            report.Totals.Single(t => t.Direction == TransferDirection.Fetched).Bytes.Should().Be(1000);
            report.Days.Should().Equal("2026-11-03");
        }

        [Test]
        public void an_hour_the_range_starts_in_should_count_in_full()
        {
            GivenRow(Day.AddHours(10), TransferDirection.Fetched, 1000, 1);

            Subject.Get(Day.AddHours(10).AddMinutes(30), Day.AddDays(1), "UTC").Totals.Single(t => t.Direction == TransferDirection.Fetched).Bytes.Should().Be(1000);
        }

        [Test]
        public void days_should_be_the_viewers_own()
        {
            // 23:00 and 01:00 on the 4th in Tokyo, the same day in UTC.
            GivenRow(Day.AddHours(14), TransferDirection.Fetched, 100, 1);
            GivenRow(Day.AddHours(16), TransferDirection.Fetched, 200, 1);

            var tokyo = Subject.Get(Day, Day.AddDays(1), "Asia/Tokyo");

            tokyo.Days.Should().Equal("2026-11-03", "2026-11-04");
            tokyo.Daily.Select(d => (d.Day, d.Bytes)).Should().Equal(("2026-11-03", 100L), ("2026-11-04", 200L));

            var utc = Subject.Get(Day, Day.AddDays(1), "UTC");

            utc.Daily.Should().ContainSingle().Which.Should().Match<DailyBytes>(d => d.Day == "2026-11-03" && d.Bytes == 300);
        }

        [Test]
        public void an_unknown_time_zone_should_count_as_utc()
        {
            GivenRow(Day.AddHours(16), TransferDirection.Fetched, 200, 1);

            var report = Subject.Get(Day, Day.AddDays(1), "Not/AZone");

            report.TimeZone.Should().Be(TimeZoneInfo.Utc.Id);
            report.Daily.Single().Day.Should().Be("2026-11-03");
        }

        [Test]
        public void daily_bytes_should_be_split_by_friend_and_app()
        {
            GivenRow(Day.AddHours(1), TransferDirection.Sent, 100, 1, peer: "alice", applicationId: 3);
            GivenRow(Day.AddHours(2), TransferDirection.Sent, 200, 1, peer: "bob", applicationId: 3);
            GivenRow(Day.AddHours(3), TransferDirection.Sent, 400, 1, peer: "bob", applicationId: 4);
            GivenRow(Day.AddHours(4), TransferDirection.Sent, 800, 1, peer: "bob", applicationId: 4);

            Subject.Get(Day, Day.AddDays(1), "UTC").Daily.Select(d => (d.PeerNodeId, d.ApplicationId, d.Bytes))
                .Should().BeEquivalentTo(new[] { ("alice", 3, 100L), ("bob", 3, 200L), ("bob", 4, 1200L) });
        }

        [Test]
        public void the_average_should_be_bytes_over_time_moving_and_the_peak_the_fastest_finished_transfer()
        {
            GivenRow(Day.AddHours(1), TransferDirection.Fetched, 1000, 10, peak: 200);
            GivenRow(Day.AddHours(2), TransferDirection.Fetched, 3000, 5, peak: 900);
            GivenRow(Day.AddHours(3), TransferDirection.Fetched, 1000, 5, failure: "DirectPathLost");
            GivenRow(Day.AddHours(3), TransferDirection.Sent, 500, 1, peak: 5000);

            var fetched = Subject.Get(Day, Day.AddDays(1), "UTC").Totals.Single(t => t.Direction == TransferDirection.Fetched);

            fetched.AverageBytesPerSecond.Should().Be(250);
            fetched.PeakBytesPerSecond.Should().Be(900);
            fetched.Finished.Should().Be(2);
            fetched.Failed.Should().Be(1);
        }

        [Test]
        public void nothing_moved_should_give_no_speed()
        {
            var fetched = Subject.Get(Day, Day.AddDays(1), "UTC").Totals.Single(t => t.Direction == TransferDirection.Fetched);

            fetched.AverageBytesPerSecond.Should().Be(0);
            fetched.PeakBytesPerSecond.Should().Be(0);
        }

        [Test]
        public void failures_should_be_grouped_by_reason_with_what_they_mean_and_where_to_read_more()
        {
            GivenRow(Day.AddHours(1), TransferDirection.Fetched, 0, 0, failure: "NoDirectRoute", transfers: 2);
            GivenRow(Day.AddHours(2), TransferDirection.Fetched, 0, 0, failure: "NoDirectRoute", transfers: 1, peer: "bob");
            GivenRow(Day.AddHours(2), TransferDirection.Sent, 0, 0, failure: "NotFound");
            GivenRow(Day.AddHours(3), TransferDirection.Sent, 0, 0, failure: "Stopped", transfers: 0);

            var failures = Subject.Get(Day, Day.AddDays(1), "UTC").Failures;

            failures.Should().HaveCount(2);
            failures[0].Should().Match<FailureCount>(f => f.Direction == TransferDirection.Fetched && f.Reason == "NoDirectRoute" && f.Count == 3 && f.GuideUrl.EndsWith("#relay-only") && f.Explanation.Contains("relay"));
            failures[1].Should().Match<FailureCount>(f => f.Direction == TransferDirection.Sent && f.Reason == "NotFound" && f.GuideUrl.EndsWith("#friends-are-refused-your-files"));
        }

        [Test]
        public void friends_should_show_each_way_their_transfers_and_how_often_the_route_was_direct()
        {
            GivenRow(Day.AddHours(1), TransferDirection.Sent, 100, 1, peer: "alice");
            GivenRow(Day.AddHours(1), TransferDirection.Fetched, 300, 1, peer: "alice");
            GivenRow(Day.AddHours(2), TransferDirection.Fetched, 0, 0, failure: "NotFound", peer: "alice");
            Db.Insert(new ConnectionStatistic { Hour = Day.AddHours(1), PeerNodeId = "alice", Direct = 3, RelayOnly = 1 });
            Db.Insert(new PeerRoute { PeerNodeId = "alice", Path = ConnectionPath.Relay, Changed = Day.AddHours(1) });

            var friends = Subject.Get(Day, Day.AddDays(1), "UTC").Friends;

            friends.Select(f => f.Name).Should().Equal("Alice", "Bob");
            friends[0].Should().Match<FriendStatistics>(f => f.Sent == 100 && f.Fetched == 300 && f.Finished == 2 && f.Failed == 1 && f.Checks == 4 && f.DirectChecks == 3 && f.Route == ConnectionPath.Relay && f.RouteChanged == Day.AddHours(1));
            friends[1].Should().Match<FriendStatistics>(f => f.Sent == 0 && f.Checks == 0 && f.Route == ConnectionPath.Unknown);
        }

        [Test]
        public void a_removed_friend_should_still_be_listed_without_a_name()
        {
            GivenRow(Day.AddHours(1), TransferDirection.Sent, 100, 1, peer: "carol");

            Subject.Get(Day, Day.AddDays(1), "UTC").Friends.Should().Contain(f => f.PeerNodeId == "carol" && f.Name == null);
        }

        [TestCase(20, 4, false)]
        [TestCase(20, 5, true)]
        [TestCase(4, 4, false)]
        public void many_requests_that_waited_should_say_the_limit_may_be_too_low(int requests, int waited, bool tooLow)
        {
            GivenRow(Day.AddHours(1), TransferDirection.Sent, 0, 0, transfers: 0, requests: requests, waited: waited, waitSeconds: waited * 60);

            var queue = Subject.Get(Day, Day.AddDays(1), "UTC").UploadQueue;

            queue.Should().Match<UploadQueueStatistics>(q => q.Requests == requests && q.Waited == waited && q.MaxUploads == 3 && q.LimitMayBeLow == tooLow);

            if (waited > 0)
            {
                queue.AverageWaitSeconds.Should().Be(60);
            }
        }

        [Test]
        public void each_app_should_show_what_it_shares_and_how_many_different_titles_were_fetched()
        {
            Db.Insert(new FetchedTitle { Hour = Day.AddHours(1), ApplicationId = 3, Title = "series-3-1" });
            Db.Insert(new FetchedTitle { Hour = Day.AddHours(2), ApplicationId = 3, Title = "series-3-1" });
            Db.Insert(new FetchedTitle { Hour = Day.AddHours(2), ApplicationId = 3, Title = "series-3-2" });
            Db.Insert(new FetchedTitle { Hour = Day.AddDays(-2), ApplicationId = 4, Title = "movie-4-1" });

            var applications = Subject.Get(Day, Day.AddDays(1), "UTC").Applications;

            applications.Select(a => (a.Name, a.SharedTitles, a.FetchedTitles)).Should().Equal(("Radarr", 40, 0), ("Sonarr", 12, 2));
        }
    }
}

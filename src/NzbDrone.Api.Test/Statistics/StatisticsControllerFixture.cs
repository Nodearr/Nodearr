using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using Nodearr.Api.V5.Statistics;
using NUnit.Framework;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Statistics;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Statistics
{
    [TestFixture]
    public class StatisticsControllerFixture : TestBase
    {
        private DateTime _from;
        private DateTime _to;
        private string _timeZone;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IStatisticsService>()
                .Setup(s => s.Get(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>()))
                .Callback<DateTime, DateTime, string>((from, to, zone) => (_from, _to, _timeZone) = (from, to, zone))
                .Returns(new StatisticsReport
                {
                    TimeZone = "Asia/Tokyo",
                    Days = new List<string> { "2026-11-03" },
                    Totals = new List<DirectionTotals> { new() { Direction = TransferDirection.Fetched, Bytes = 4000, Finished = 2, Failed = 1, AverageBytesPerSecond = 250, PeakBytesPerSecond = 900 } },
                    Friends = new List<FriendStatistics> { new() { PeerNodeId = "alice", Name = "Alice", Checks = 4, DirectChecks = 3, Route = ConnectionPath.Relay } },
                    UploadQueue = new UploadQueueStatistics { Requests = 20, Waited = 5, MaxUploads = 3, LimitMayBeLow = true }
                });
        }

        private StatisticsController Subject => new(Mocker.GetMock<IStatisticsService>().Object);

        [Test]
        public void without_a_range_it_should_be_the_last_30_days()
        {
            Subject.GetStatistics();

            _to.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            (_to - _from).Should().Be(TimeSpan.FromDays(StatisticsController.DefaultDays));
            _timeZone.Should().BeEmpty();
        }

        [Test]
        public void a_range_and_time_zone_should_be_passed_on_as_given()
        {
            var from = new DateTime(2026, 10, 4, 15, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

            Subject.GetStatistics(from, to, "Asia/Tokyo");

            (_from, _to, _timeZone).Should().Be((from, to, "Asia/Tokyo"));
        }

        [Test]
        public void what_the_page_shows_should_come_back_as_it_was_worked_out()
        {
            var resource = Subject.GetStatistics().Value;

            resource.TimeZone.Should().Be("Asia/Tokyo");
            resource.Days.Should().Equal("2026-11-03");
            resource.Totals.Should().ContainSingle().Which.Should().Match<DirectionTotalsResource>(t => t.Bytes == 4000 && t.Finished == 2 && t.Failed == 1 && t.AverageBytesPerSecond == 250 && t.PeakBytesPerSecond == 900);
            resource.Friends.Should().ContainSingle().Which.Should().Match<FriendStatisticsResource>(f => f.Name == "Alice" && f.Checks == 4 && f.DirectChecks == 3 && f.Route == ConnectionPath.Relay);
            resource.UploadQueue.Should().Match<UploadQueueResource>(q => q.Requests == 20 && q.Waited == 5 && q.LimitMayBeLow);
        }
    }
}

using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class TrimStatisticsFixture : DbTest<TrimStatistics, TransferStatistic>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ITransferStatisticRepository>(Mocker.Resolve<TransferStatisticRepository>());
            Mocker.SetConstant<IConnectionStatisticRepository>(Mocker.Resolve<ConnectionStatisticRepository>());
            Mocker.SetConstant<IFetchedTitleRepository>(Mocker.Resolve<FetchedTitleRepository>());
        }

        private static DateTime Now => StatisticsHours.HourOf(DateTime.UtcNow);

        private void GivenHour(DateTime hour, string tag)
        {
            Db.Insert(new TransferStatistic { Hour = hour, PeerNodeId = tag, Failure = string.Empty });
            Db.Insert(new ConnectionStatistic { Hour = hour, PeerNodeId = tag });
            Db.Insert(new FetchedTitle { Hour = hour, Title = tag });
        }

        [Test]
        public void hours_older_than_13_months_should_be_removed()
        {
            GivenHour(Now.AddMonths(-14), "old");
            GivenHour(Now.AddMonths(-13).AddDays(-1), "just-too-old");
            GivenHour(Now.AddMonths(-13).AddDays(1), "kept");
            GivenHour(Now, "new");

            Subject.Clean();

            AllStoredModels.Where(s => s.Hour != StatisticsHours.Carried).Select(s => s.PeerNodeId).Should().BeEquivalentTo("kept", "new");
            Mocker.Resolve<IConnectionStatisticRepository>().All().Where(s => s.Hour != StatisticsHours.Carried).Select(s => s.PeerNodeId).Should().BeEquivalentTo("kept", "new");
            Mocker.Resolve<IFetchedTitleRepository>().All().Select(s => s.Title).Should().BeEquivalentTo("kept", "new");
        }

        // /metrics gives totals since the start. Prometheus reads one that goes down as a restart.
        [Test]
        public void the_totals_since_the_start_should_not_change()
        {
            Db.Insert(new TransferStatistic { Hour = Now.AddMonths(-15), PeerNodeId = "alice", ApplicationId = 1, Failure = string.Empty, Bytes = 100, Transfers = 1, Seconds = 2, PeakBytesPerSecond = 50 });
            Db.Insert(new TransferStatistic { Hour = Now.AddMonths(-14), PeerNodeId = "alice", ApplicationId = 1, Failure = string.Empty, Bytes = 300, Transfers = 2, Seconds = 3, PeakBytesPerSecond = 150 });
            Db.Insert(new TransferStatistic { Hour = Now.AddMonths(-14), PeerNodeId = "alice", ApplicationId = 1, Failure = "NotFound", Transfers = 2, Reopened = 1 });
            Db.Insert(new TransferStatistic { Hour = Now, PeerNodeId = "alice", ApplicationId = 1, Failure = string.Empty, Bytes = 1000, Transfers = 1 });
            Db.Insert(new ConnectionStatistic { Hour = Now.AddMonths(-15), PeerNodeId = "alice", Direct = 3, RelayOnly = 1 });
            Db.Insert(new ConnectionStatistic { Hour = Now.AddMonths(-14), PeerNodeId = "alice", Direct = 2, None = 1, RouteChanges = 1 });

            Subject.Clean();

            // Twice, so what was already carried is added to and not counted again.
            Db.Insert(new TransferStatistic { Hour = Now.AddMonths(-14).AddHours(1), PeerNodeId = "alice", ApplicationId = 1, Failure = string.Empty, Bytes = 600, Transfers = 1, Seconds = 1, PeakBytesPerSecond = 600 });
            Subject.Clean();

            var carried = AllStoredModels.Where(s => s.Hour == StatisticsHours.Carried).ToList();

            carried.Single(s => s.IsFinished).Should().Match<TransferStatistic>(s => s.Bytes == 1000 && s.Transfers == 4 && s.Seconds == 6 && s.PeakBytesPerSecond == 600 && s.PeerNodeId == "alice" && s.ApplicationId == 1);
            carried.Single(s => !s.IsFinished).Should().Match<TransferStatistic>(s => s.Failure == "NotFound" && s.Transfers == 2 && s.Reopened == 1);
            AllStoredModels.Sum(s => s.Bytes).Should().Be(2000);

            Mocker.Resolve<IConnectionStatisticRepository>().All().Should().ContainSingle()
                .Which.Should().Match<ConnectionStatistic>(s => s.Hour == StatisticsHours.Carried && s.Direct == 5 && s.RelayOnly == 1 && s.None == 1 && s.RouteChanges == 1);
        }

        [Test]
        public void the_carried_totals_should_be_left_out_of_the_page()
        {
            Db.Insert(new TransferStatistic { Hour = Now.AddMonths(-14), PeerNodeId = "alice", Failure = string.Empty, Bytes = 100, Transfers = 1 });

            Subject.Clean();

            Mocker.Resolve<ITransferStatisticRepository>().Between(Now.AddMonths(-12), Now.AddHours(1)).Should().BeEmpty();
        }
    }
}

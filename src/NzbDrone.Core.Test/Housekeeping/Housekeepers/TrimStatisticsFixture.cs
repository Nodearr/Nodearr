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

        private void GivenHour(DateTime hour, string tag)
        {
            Db.Insert(new TransferStatistic { Hour = hour, PeerNodeId = tag, Failure = string.Empty });
            Db.Insert(new ConnectionStatistic { Hour = hour, PeerNodeId = tag });
            Db.Insert(new FetchedTitle { Hour = hour, Title = tag });
        }

        [Test]
        public void rows_older_than_13_months_should_be_removed()
        {
            var now = StatisticsHours.HourOf(DateTime.UtcNow);

            GivenHour(now.AddMonths(-14), "old");
            GivenHour(now.AddMonths(-13).AddDays(-1), "just-too-old");
            GivenHour(now.AddMonths(-13).AddDays(1), "kept");
            GivenHour(now, "new");

            Subject.Clean();

            AllStoredModels.Select(s => s.PeerNodeId).Should().BeEquivalentTo("kept", "new");
            Mocker.Resolve<IConnectionStatisticRepository>().All().Select(s => s.PeerNodeId).Should().BeEquivalentTo("kept", "new");
            Mocker.Resolve<IFetchedTitleRepository>().All().Select(s => s.Title).Should().BeEquivalentTo("kept", "new");
        }
    }
}

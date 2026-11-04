using System;
using NzbDrone.Core.Statistics;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // The statistics are kept 13 months, so a year can be compared with the one before it.
    public class TrimStatistics : IHousekeepingTask
    {
        private readonly ITransferStatisticRepository _transfers;
        private readonly IConnectionStatisticRepository _connections;
        private readonly IFetchedTitleRepository _fetchedTitles;

        public TrimStatistics(ITransferStatisticRepository transfers, IConnectionStatisticRepository connections, IFetchedTitleRepository fetchedTitles)
        {
            _transfers = transfers;
            _connections = connections;
            _fetchedTitles = fetchedTitles;
        }

        public void Clean()
        {
            var before = StatisticsHours.HourOf(DateTime.UtcNow.AddMonths(-StatisticsHours.MonthsKept));

            _transfers.DeleteBefore(before);
            _connections.DeleteBefore(before);
            _fetchedTitles.DeleteBefore(before);
        }
    }
}

using System;
using NzbDrone.Core.Statistics;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // The hourly statistics are kept 13 months, so a year can be compared with the one before it.
    // Older hours are added to one carried row each, so the totals since the start, which
    // /metrics gives, never go down. Titles fetched are only ever counted within a range, so
    // those are simply removed.
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

            _transfers.CarryBefore(before);
            _connections.CarryBefore(before);
            _fetchedTitles.DeleteBefore(before);
        }
    }
}

using System;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;

namespace NzbDrone.Core.Analytics
{
    public interface IAnalyticsService
    {
        bool IsEnabled { get; }
        bool InstallIsActive { get; }
    }

    public class AnalyticsService : IAnalyticsService
    {
        private readonly IHistoryService _historyService;

        public AnalyticsService(IHistoryService historyService)
        {
            _historyService = historyService;
        }

        // Nodearr collects nothing. The code this came from loaded a tracking script in the UI
        // and reported UI errors when this was true, and it was always true in development builds.
        public bool IsEnabled => false;

        public bool InstallIsActive
        {
            get
            {
                var lastRecord = _historyService.Paged(new PagingSpec<EpisodeHistory>() { Page = 0, PageSize = 1, SortKey = "date", SortDirection = SortDirection.Descending }, null, null);
                var monthAgo = DateTime.UtcNow.AddMonths(-1);

                return lastRecord.Records.Any(v => v.Date > monthAgo);
            }
        }
    }
}

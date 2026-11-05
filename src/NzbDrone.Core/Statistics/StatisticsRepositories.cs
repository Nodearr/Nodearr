using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Statistics
{
    public interface ITransferStatisticRepository : IBasicRepository<TransferStatistic>
    {
        TransferStatistic Find(DateTime hour, string peerNodeId, int applicationId, TransferDirection direction, string failure);
        List<TransferStatistic> Between(DateTime from, DateTime to);

        // Everything since the start, added up for each friend, Arr app, direction and ending.
        // Hour is left unset. The longest wait and fastest transfer are not given.
        List<TransferStatistic> Totals();

        // Adds the hours before this one to the carried totals (StatisticsHours.Carried), and
        // removes them.
        void CarryBefore(DateTime hour);
    }

    public class TransferStatisticRepository : BasicRepository<TransferStatistic>, ITransferStatisticRepository
    {
        public TransferStatisticRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public TransferStatistic Find(DateTime hour, string peerNodeId, int applicationId, TransferDirection direction, string failure)
        {
            return Query(s => s.Hour == hour && s.PeerNodeId == peerNodeId && s.ApplicationId == applicationId && s.Direction == direction && s.Failure == failure).FirstOrDefault();
        }

        public List<TransferStatistic> Between(DateTime from, DateTime to)
        {
            return Query(s => s.Hour >= from && s.Hour < to);
        }

        public List<TransferStatistic> Totals()
        {
            using var connection = _database.OpenConnection();

            return connection
                .Query<TransferTotal>($"SELECT \"PeerNodeId\", \"ApplicationId\", \"Direction\", \"Failure\", SUM(\"Bytes\") AS \"Bytes\", SUM(\"Transfers\") AS \"Transfers\", SUM(\"Reopened\") AS \"Reopened\", CAST(SUM(\"Seconds\") AS DOUBLE PRECISION) AS \"Seconds\", SUM(\"Requests\") AS \"Requests\", SUM(\"Waited\") AS \"Waited\", CAST(SUM(\"WaitSeconds\") AS DOUBLE PRECISION) AS \"WaitSeconds\" FROM \"{_table}\" GROUP BY \"PeerNodeId\", \"ApplicationId\", \"Direction\", \"Failure\"")
                .Select(t => new TransferStatistic
                {
                    PeerNodeId = t.PeerNodeId,
                    ApplicationId = t.ApplicationId,
                    Direction = (TransferDirection)t.Direction,
                    Failure = t.Failure,
                    Bytes = t.Bytes,
                    Transfers = (int)t.Transfers,
                    Reopened = (int)t.Reopened,
                    Seconds = t.Seconds,
                    Requests = (int)t.Requests,
                    Waited = (int)t.Waited,
                    WaitSeconds = t.WaitSeconds
                })
                .ToList();
        }

        public void CarryBefore(DateTime hour)
        {
            var carried = StatisticsHours.Carried;
            var after = carried.AddHours(1);
            var old = Between(after, hour);

            if (old.Count == 0)
            {
                return;
            }

            var totals = Between(carried, after).ToDictionary(Key);

            foreach (var row in old)
            {
                if (!totals.TryGetValue(Key(row), out var total))
                {
                    total = new TransferStatistic { Hour = carried, PeerNodeId = row.PeerNodeId, ApplicationId = row.ApplicationId, Direction = row.Direction, Failure = row.Failure };
                    totals[Key(row)] = total;
                }

                total.Bytes += row.Bytes;
                total.Transfers += row.Transfers;
                total.Reopened += row.Reopened;
                total.Seconds += row.Seconds;
                total.PeakBytesPerSecond = Math.Max(total.PeakBytesPerSecond, row.PeakBytesPerSecond);
                total.Requests += row.Requests;
                total.Waited += row.Waited;
                total.WaitSeconds += row.WaitSeconds;
                total.LongestWaitSeconds = Math.Max(total.LongestWaitSeconds, row.LongestWaitSeconds);
            }

            DeleteAndUpsert(s => s.Hour >= after && s.Hour < hour, totals.Values.ToList());
        }

        private static (string PeerNodeId, int ApplicationId, TransferDirection Direction, string Failure) Key(TransferStatistic s) => (s.PeerNodeId, s.ApplicationId, s.Direction, s.Failure);

        private class TransferTotal
        {
            public string PeerNodeId { get; set; }
            public int ApplicationId { get; set; }
            public int Direction { get; set; }
            public string Failure { get; set; }
            public long Bytes { get; set; }
            public long Transfers { get; set; }
            public long Reopened { get; set; }
            public double Seconds { get; set; }
            public long Requests { get; set; }
            public long Waited { get; set; }
            public double WaitSeconds { get; set; }
        }
    }

    public interface IConnectionStatisticRepository : IBasicRepository<ConnectionStatistic>
    {
        ConnectionStatistic Find(DateTime hour, string peerNodeId);
        List<ConnectionStatistic> Between(DateTime from, DateTime to);

        // Every check since the start, added up for each friend. Hour is left unset.
        List<ConnectionStatistic> Totals();

        // Adds the hours before this one to the carried totals (StatisticsHours.Carried), and
        // removes them.
        void CarryBefore(DateTime hour);
    }

    public class ConnectionStatisticRepository : BasicRepository<ConnectionStatistic>, IConnectionStatisticRepository
    {
        public ConnectionStatisticRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public ConnectionStatistic Find(DateTime hour, string peerNodeId)
        {
            return Query(s => s.Hour == hour && s.PeerNodeId == peerNodeId).FirstOrDefault();
        }

        public List<ConnectionStatistic> Between(DateTime from, DateTime to)
        {
            return Query(s => s.Hour >= from && s.Hour < to);
        }

        public List<ConnectionStatistic> Totals()
        {
            using var connection = _database.OpenConnection();

            return connection
                .Query<ConnectionTotal>($"SELECT \"PeerNodeId\", SUM(\"Direct\") AS \"Direct\", SUM(\"RelayOnly\") AS \"RelayOnly\", SUM(\"None\") AS \"None\", SUM(\"RouteChanges\") AS \"RouteChanges\" FROM \"{_table}\" GROUP BY \"PeerNodeId\"")
                .Select(t => new ConnectionStatistic { PeerNodeId = t.PeerNodeId, Direct = (int)t.Direct, RelayOnly = (int)t.RelayOnly, None = (int)t.None, RouteChanges = (int)t.RouteChanges })
                .ToList();
        }

        public void CarryBefore(DateTime hour)
        {
            var carried = StatisticsHours.Carried;
            var after = carried.AddHours(1);
            var old = Between(after, hour);

            if (old.Count == 0)
            {
                return;
            }

            var totals = Between(carried, after).ToDictionary(s => s.PeerNodeId);

            foreach (var row in old)
            {
                if (!totals.TryGetValue(row.PeerNodeId, out var total))
                {
                    total = new ConnectionStatistic { Hour = carried, PeerNodeId = row.PeerNodeId };
                    totals[row.PeerNodeId] = total;
                }

                total.Direct += row.Direct;
                total.RelayOnly += row.RelayOnly;
                total.None += row.None;
                total.RouteChanges += row.RouteChanges;
            }

            DeleteAndUpsert(s => s.Hour >= after && s.Hour < hour, totals.Values.ToList());
        }

        private class ConnectionTotal
        {
            public string PeerNodeId { get; set; }
            public long Direct { get; set; }
            public long RelayOnly { get; set; }
            public long None { get; set; }
            public long RouteChanges { get; set; }
        }
    }

    public interface IPeerRouteRepository : IBasicRepository<PeerRoute>
    {
        PeerRoute FindByNodeId(string peerNodeId);
    }

    public class PeerRouteRepository : BasicRepository<PeerRoute>, IPeerRouteRepository
    {
        public PeerRouteRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public PeerRoute FindByNodeId(string peerNodeId)
        {
            return Query(r => r.PeerNodeId == peerNodeId).FirstOrDefault();
        }
    }

    public interface IFetchedTitleRepository : IBasicRepository<FetchedTitle>
    {
        bool Exists(DateTime hour, int applicationId, string title);

        // How many different titles were fetched in the range, for each Arr app.
        Dictionary<int, int> CountDistinct(DateTime from, DateTime to);
        void DeleteBefore(DateTime hour);
    }

    public class FetchedTitleRepository : BasicRepository<FetchedTitle>, IFetchedTitleRepository
    {
        public FetchedTitleRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public bool Exists(DateTime hour, int applicationId, string title)
        {
            return Query(t => t.Hour == hour && t.ApplicationId == applicationId && t.Title == title).Any();
        }

        public Dictionary<int, int> CountDistinct(DateTime from, DateTime to)
        {
            using var connection = _database.OpenConnection();

            return connection
                .Query<TitleCount>($"SELECT \"ApplicationId\", COUNT(DISTINCT \"Title\") AS \"Count\" FROM \"{_table}\" WHERE \"Hour\" >= @from AND \"Hour\" < @to GROUP BY \"ApplicationId\"", new { from, to })
                .ToDictionary(c => c.ApplicationId, c => (int)c.Count);
        }

        public void DeleteBefore(DateTime hour)
        {
            Delete(t => t.Hour < hour);
        }

        private class TitleCount
        {
            public int ApplicationId { get; set; }
            public long Count { get; set; }
        }
    }
}

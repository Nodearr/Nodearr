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
        void DeleteBefore(DateTime hour);
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

        public void DeleteBefore(DateTime hour)
        {
            Delete(s => s.Hour < hour);
        }
    }

    public interface IConnectionStatisticRepository : IBasicRepository<ConnectionStatistic>
    {
        ConnectionStatistic Find(DateTime hour, string peerNodeId);
        List<ConnectionStatistic> Between(DateTime from, DateTime to);
        void DeleteBefore(DateTime hour);
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

        public void DeleteBefore(DateTime hour)
        {
            Delete(s => s.Hour < hour);
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

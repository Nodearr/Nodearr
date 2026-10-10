using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.PeerCatalog
{
    public interface IPeerCatalogRepository : IBasicRepository<PeerCatalogItem>
    {
        List<PeerCatalogItem> ForPeer(int peerId);
        void DeleteForPeer(int peerId);
        Dictionary<(int PeerId, PeerItemStatus Status), int> CountByStatus();
    }

    public class PeerCatalogRepository : BasicRepository<PeerCatalogItem>, IPeerCatalogRepository
    {
        public PeerCatalogRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<PeerCatalogItem> ForPeer(int peerId)
        {
            return Query(i => i.PeerId == peerId);
        }

        public void DeleteForPeer(int peerId)
        {
            Delete(i => i.PeerId == peerId);
        }

        public Dictionary<(int PeerId, PeerItemStatus Status), int> CountByStatus()
        {
            using var connection = _database.OpenConnection();

            return connection
                .Query<StatusCount>($"SELECT \"PeerId\", \"LocalStatus\", COUNT(*) AS \"Count\" FROM \"{_table}\" GROUP BY \"PeerId\", \"LocalStatus\"")
                .ToDictionary(r => (r.PeerId, (PeerItemStatus)r.LocalStatus), r => (int)r.Count);
        }

        private class StatusCount
        {
            public int PeerId { get; set; }
            public int LocalStatus { get; set; }
            public long Count { get; set; }
        }
    }
}

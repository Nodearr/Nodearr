using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.PeerCatalog
{
    public interface IPeerCatalogRepository : IBasicRepository<PeerCatalogItem>
    {
        List<PeerCatalogItem> ForPeer(int peerId);
        PeerCatalogItem FindByCandidateId(string candidateId);
        List<PeerCatalogItem> FindByMediaIds(MediaKind kind, int tvdbId, int tmdbId, string imdbId);
        List<PeerCatalogItem> FindByText(string word);
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

        public PeerCatalogItem FindByCandidateId(string candidateId)
        {
            return Query(i => i.CandidateId == candidateId).FirstOrDefault();
        }

        // Everything of one kind that carries any of the given IDs. Zero and null mean "not given".
        public List<PeerCatalogItem> FindByMediaIds(MediaKind kind, int tvdbId, int tmdbId, string imdbId)
        {
            var found = new Dictionary<int, PeerCatalogItem>();

            if (tvdbId > 0)
            {
                Query(i => i.MediaKind == kind && i.TvdbId == tvdbId).ForEach(i => found[i.Id] = i);
            }

            if (tmdbId > 0)
            {
                Query(i => i.MediaKind == kind && i.TmdbId == tmdbId).ForEach(i => found[i.Id] = i);
            }

            if (imdbId != null)
            {
                Query(i => i.MediaKind == kind && i.ImdbId == imdbId).ForEach(i => found[i.Id] = i);
            }

            return found.Values.ToList();
        }

        public List<PeerCatalogItem> FindByText(string word)
        {
            return Query(i => i.ReleaseTitle.Contains(word) || i.Title.Contains(word));
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

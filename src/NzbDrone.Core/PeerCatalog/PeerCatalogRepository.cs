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

        // Albums whose artist, title or release title holds the word.
        List<PeerCatalogItem> FindAlbumsByText(string word);
        List<PeerCatalogItem> FindByText(string word);

        // The adult content released on a day, given as yyyy-MM-dd. Whisparr searches for a scene by its day.
        List<PeerCatalogItem> FindAdultByReleaseDate(string releaseDate);

        // Every peer's copy of the file with this hash.
        List<PeerCatalogItem> FindByBlake3(string blake3);
        void DeleteForPeer(int peerId);
        void DeleteForPeer(int peerId, IEnumerable<string> remoteIds);

        // What is held for a peer, as the peer names each file and with its fingerprint.
        List<(string RemoteId, string Fingerprint)> FingerprintsForPeer(int peerId);
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
            return Query(i => i.ReleaseTitle.Contains(word) || i.Title.Contains(word) || i.Studio.Contains(word) || i.Code.Contains(word));
        }

        public List<PeerCatalogItem> FindAlbumsByText(string word)
        {
            return Query(i => i.MediaKind == MediaKind.Music && (i.Artist.Contains(word) || i.Title.Contains(word) || i.ReleaseTitle.Contains(word)));
        }

        public List<PeerCatalogItem> FindAdultByReleaseDate(string releaseDate)
        {
            return releaseDate == null ? new List<PeerCatalogItem>() : Query(i => i.IsAdult && i.ReleaseDate == releaseDate);
        }

        public List<PeerCatalogItem> FindByBlake3(string blake3)
        {
            return blake3 == null ? new List<PeerCatalogItem>() : Query(i => i.Blake3 == blake3);
        }

        public void DeleteForPeer(int peerId)
        {
            Delete(i => i.PeerId == peerId);
        }

        public void DeleteForPeer(int peerId, IEnumerable<string> remoteIds)
        {
            // In batches, to stay well inside SQLite's limit on query parameters.
            foreach (var batch in remoteIds.Distinct().Chunk(500))
            {
                var ids = batch.ToList();

                Delete(i => i.PeerId == peerId && ids.Contains(i.RemoteId));
            }
        }

        public List<(string RemoteId, string Fingerprint)> FingerprintsForPeer(int peerId)
        {
            using var connection = _database.OpenConnection();

            return connection
                .Query<(string RemoteId, string Fingerprint)>($"SELECT \"RemoteId\", \"Fingerprint\" FROM \"{_table}\" WHERE \"PeerId\" = @peerId", new { peerId })
                .ToList();
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

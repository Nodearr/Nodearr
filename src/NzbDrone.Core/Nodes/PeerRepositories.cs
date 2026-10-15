using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Nodes
{
    public interface IPeerRepository : IBasicRepository<Peer>
    {
        Peer FindByNodeId(string nodeId);
    }

    public class PeerRepository : BasicRepository<Peer>, IPeerRepository
    {
        public PeerRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Peer FindByNodeId(string nodeId)
        {
            return Query(p => p.NodeId == nodeId).FirstOrDefault();
        }
    }

    public interface IPeerInviteRepository : IBasicRepository<PeerInvite>
    {
        PeerInvite FindBySecretHash(string secretHash);
    }

    public class PeerInviteRepository : BasicRepository<PeerInvite>, IPeerInviteRepository
    {
        public PeerInviteRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public PeerInvite FindBySecretHash(string secretHash)
        {
            return Query(i => i.SecretHash == secretHash).FirstOrDefault();
        }
    }
}

using System;
using System.Linq;
using Dapper;
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

        // Marks the invite redeemed by this node, if nothing has redeemed it yet. False when
        // something else got there first.
        bool TryRedeem(int id, string nodeId, DateTime redeemedAt);
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

        // One statement, so two pair requests with the same secret cannot both see the invite
        // unused and both redeem it: the database lets only one of them change the row.
        public bool TryRedeem(int id, string nodeId, DateTime redeemedAt)
        {
            using var connection = _database.OpenConnection();

            return connection.Execute(
                $"UPDATE \"{_table}\" SET \"RedeemedAt\" = @redeemedAt, \"RedeemedByNodeId\" = @nodeId WHERE \"Id\" = @id AND \"RedeemedAt\" IS NULL",
                new { id, nodeId, redeemedAt }) == 1;
        }
    }
}

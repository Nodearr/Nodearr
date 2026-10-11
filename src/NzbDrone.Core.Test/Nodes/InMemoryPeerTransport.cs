using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Test.Nodes
{
    // The network that in-memory transports share. It plays the part iroh plays for real: it
    // delivers messages and vouches for who sent them.
    public class InMemoryNetwork
    {
        private readonly Dictionary<string, InMemoryPeerTransport> _nodes = new();

        public InMemoryPeerTransport Join(string nodeId)
        {
            var transport = new InMemoryPeerTransport(this, nodeId);

            _nodes[nodeId] = transport;

            return transport;
        }

        public InMemoryPeerTransport Find(string nodeId)
        {
            return _nodes.GetValueOrDefault(nodeId);
        }
    }

    public class InMemoryPeerTransport : IPeerTransport
    {
        private readonly InMemoryNetwork _network;
        private readonly string _nodeId;
        private HashSet<string> _blocked = new();
        private IncomingMessageHandler _handler;

        public InMemoryPeerTransport(InMemoryNetwork network, string nodeId)
        {
            _network = network;
            _nodeId = nodeId;
        }

        public bool IsRunning { get; set; } = true;

        // How many messages reached this node's handler, so a test can tell "refused at the door"
        // from "let in and refused".
        public int Delivered { get; private set; }

        public NodeAddress GetLocalAddress()
        {
            if (!IsRunning)
            {
                throw new TransportException(TransportFailure.Unavailable, "not running");
            }

            return new NodeAddress { NodeId = _nodeId, RelayUrl = "memory://relay", IpAddresses = new List<string> { "127.0.0.1:1" } };
        }

        public byte[] Request(NodeAddress peer, byte[] body, TimeSpan timeout)
        {
            var target = _network.Find(peer.NodeId);

            if (target is not { IsRunning: true })
            {
                throw new TransportException(TransportFailure.NoRoute, "no route");
            }

            if (target._blocked.Contains(_nodeId))
            {
                throw new TransportException(TransportFailure.Blocked, "blocked");
            }

            target.Delivered++;

            return target._handler?.Invoke(_nodeId, body) ?? throw new TransportException(TransportFailure.NotAuthorized, "refused");
        }

        // Every route on this network is a direct one, unless a test says otherwise.
        public ConnectionPath PathToOthers { get; set; } = ConnectionPath.Direct;

        public ConnectionProbe Probe(NodeAddress peer, TimeSpan waitForDirect)
        {
            var target = _network.Find(peer.NodeId);

            if (target is not { IsRunning: true })
            {
                throw new TransportException(TransportFailure.NoRoute, "no route");
            }

            if (target._blocked.Contains(_nodeId))
            {
                throw new TransportException(TransportFailure.Blocked, "blocked");
            }

            return new ConnectionProbe { Path = PathToOthers, RemoteAddress = PathToOthers == ConnectionPath.Direct ? "127.0.0.1:1" : "memory://relay" };
        }

        public NetworkStatus GetNetworkStatus()
        {
            return new NetworkStatus { RelayUrl = "memory://relay", IpAddresses = new List<string> { "127.0.0.1:1" }, Udp = true, MappingVaries = false };
        }

        public void SetBlocked(IEnumerable<string> nodeIds)
        {
            _blocked = nodeIds.ToHashSet();
        }

        public void SetHandler(IncomingMessageHandler handler)
        {
            _handler = handler;
        }
    }
}

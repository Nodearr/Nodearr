using System;
using System.Collections.Generic;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Nodes.Transport
{
    // Where a node can be reached. The node ID is its public key, so an address that lies about
    // the ID simply fails to connect.
    public class NodeAddress
    {
        public string NodeId { get; set; }
        public string RelayUrl { get; set; }
        public List<string> IpAddresses { get; set; } = new();
    }

    public enum TransportFailure
    {
        // The transport itself is not running.
        Unavailable,
        NoRoute,
        Timeout,

        // The peer refused the connection because it has blocked this node.
        Blocked,

        // The peer accepted the connection and refused the message.
        NotAuthorized,
        Failed
    }

    public class TransportException : NzbDroneException
    {
        public TransportException(TransportFailure failure, string message)
            : base(message)
        {
            Failure = failure;
        }

        public TransportFailure Failure { get; }
    }

    // How a connection to another node travels. Files only ever move over a direct one.
    public enum ConnectionPath
    {
        Unknown = 0,

        // Straight between the two nodes.
        Direct = 1,

        // Through a relay. Enough for messages. A file transfer would fail as NoDirectRoute.
        Relay = 2
    }

    public class ConnectionProbe
    {
        public ConnectionPath Path { get; set; }

        // The other node's address on a direct path, or the relay on a relayed one.
        public string RemoteAddress { get; set; }
    }

    // What the transport has worked out about the network this node is on. Everything except
    // the addresses is unknown until its first look is done, and when relays are off.
    public class NetworkStatus
    {
        public string RelayUrl { get; set; }
        public List<string> IpAddresses { get; set; } = new();

        // Whether UDP gets out at all. Nothing works without it.
        public bool? Udp { get; set; }

        // True when the network gives a different public port for each destination. Two nodes can
        // rarely find a direct path through that without a forwarded port.
        public bool? MappingVaries { get; set; }

        // This node's address as the outside sees it.
        public string PublicAddress { get; set; }
        public bool? CaptivePortal { get; set; }
    }

    // Raised when the transport starts or stops, so its health check runs again.
    public class PeerTransportStateChangedEvent : IEvent
    {
    }

    // Receives a message from a node whose identity the transport has verified.
    // Returning null refuses the message.
    public delegate byte[] IncomingMessageHandler(string remoteNodeId, byte[] body);

    // Carries opaque messages between nodes. Trust decisions belong to the caller, except for the
    // block list, which the transport applies before a blocked node can send anything.
    public interface IPeerTransport
    {
        bool IsRunning { get; }

        // Stops the transport and starts it again, so that it takes settings that changed. The
        // node keeps its identity. Transfers under way stop and are picked up again.
        void Restart();
        NodeAddress GetLocalAddress();
        byte[] Request(NodeAddress peer, byte[] body, TimeSpan timeout);

        // Connects to a node and waits up to that long for a direct path to it to appear.
        ConnectionProbe Probe(NodeAddress peer, TimeSpan waitForDirect);
        NetworkStatus GetNetworkStatus();
        void SetBlocked(IEnumerable<string> nodeIds);
        void SetHandler(IncomingMessageHandler handler);
    }
}

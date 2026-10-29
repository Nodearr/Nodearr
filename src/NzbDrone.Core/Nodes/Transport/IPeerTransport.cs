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

        // A message that may not cross a relay, or its answer, found no direct path, and nothing of
        // it was sent. Also when one came through a relay and was refused.
        NoDirectRoute,
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

        // What a friend shows when its catalog, a poster or a sent title could not move for want of
        // a direct path.
        public const string NoDirectRouteReason = "NoDirectRoute: Needs a direct connection. This friend's node is on an older Nodearr, or reachable only through a relay";
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

    // Receives a message from a node whose identity the transport has verified. arrivedDirect is
    // false when anything on the connection it came on has come through a relay, which includes
    // any of the message itself. Returning null refuses the message.
    public delegate IncomingAnswer IncomingMessageHandler(string remoteNodeId, byte[] body, bool arrivedDirect);

    // How a message from another node is answered.
    public class IncomingAnswer
    {
        public byte[] Body { get; init; }

        // The answer may not cross a relay. The transport waits for a direct path to send it on,
        // and sends nothing if none appears.
        public bool Direct { get; init; }

        // Refused because it came through a relay when it may not. The other node is told so, and
        // the connection stays open for what may cross one.
        public bool NoDirectRoute { get; init; }
    }

    // Carries opaque messages between nodes. Trust decisions belong to the caller, except for the
    // block list, which the transport applies before a blocked node can send anything.
    public interface IPeerTransport
    {
        bool IsRunning { get; }

        // Stops the transport and starts it again, so that it takes settings that changed. The
        // node keeps its identity. Transfers under way stop and are picked up again.
        void Restart();
        NodeAddress GetLocalAddress();

        // Sends a message and waits for its answer. With direct, neither may cross a relay: the
        // transport waits for a direct path first, and fails as NoDirectRoute if none appears.
        byte[] Request(NodeAddress peer, byte[] body, TimeSpan timeout, bool direct = false);

        // Connects to a node and waits up to that long for a direct path to it to appear.
        ConnectionProbe Probe(NodeAddress peer, TimeSpan waitForDirect);
        NetworkStatus GetNetworkStatus();

        // Applies the block list, and closes whatever is already open to a node on it.
        void SetBlocked(IEnumerable<string> nodeIds);

        // Closes the connections the transport opened to a node and keeps open while they are
        // talking, including any it has replaced. Used when trust ends, so nothing stays open to it
        // until it would have closed for being idle.
        void CloseConnection(string nodeId);
        void SetHandler(IncomingMessageHandler handler);
    }
}

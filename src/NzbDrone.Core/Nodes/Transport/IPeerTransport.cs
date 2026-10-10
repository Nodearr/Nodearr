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
        NodeAddress GetLocalAddress();
        byte[] Request(NodeAddress peer, byte[] body, TimeSpan timeout);
        void SetBlocked(IEnumerable<string> nodeIds);
        void SetHandler(IncomingMessageHandler handler);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Nodes
{
    public class PeerException : NzbDroneException
    {
        public PeerException(string message)
            : base(message)
        {
        }
    }

    public class NodeIdentity
    {
        public bool TransportRunning { get; set; }
        public string NodeId { get; set; }
        public string Name { get; set; }
        public string RelayUrl { get; set; }
        public List<string> IpAddresses { get; set; } = new();
    }

    public class CreatedInvite
    {
        public PeerInvite Invite { get; set; }

        // Shown once. Nodearr keeps only a hash of the secret inside it.
        public string Code { get; set; }
    }

    public interface IPeerService
    {
        NodeIdentity GetIdentity();
        List<Peer> All();
        List<PeerInvite> Invites();
        CreatedInvite CreateInvite(TimeSpan validFor, string label, bool shareAll = false);

        // Sends a message to a trusted node and returns its answer.
        PeerReply Ask(Peer peer, PeerMessage message);
        void DeleteInvite(int id);
        Peer RedeemInvite(string code);
        Peer Ping(int id);
        Peer Block(int id);
        Peer Unblock(int id);
        Peer Revoke(int id);
        void Delete(int id);
    }

    public class PeerService : IPeerService, IHandle<ApplicationStartedEvent>, IHandleAsync<PeerTransportStateChangedEvent>
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

        private readonly IPeerRepository _peers;
        private readonly IPeerInviteRepository _invites;
        private readonly IPeerTransport _transport;
        private readonly IOfferExchange _offerExchange;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public PeerService(IPeerRepository peers, IPeerInviteRepository invites, IPeerTransport transport, IOfferExchange offerExchange, IEventAggregator eventAggregator, Logger logger)
        {
            _peers = peers;
            _invites = invites;
            _transport = transport;
            _offerExchange = offerExchange;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        // Stands in for a display-name setting until there is one.
        public virtual string LocalName => Environment.MachineName;

        public void Handle(ApplicationStartedEvent message)
        {
            _transport.SetHandler(HandleIncoming);
            PushBlocked();
        }

        // A node's direct addresses change when its transport restarts. Telling trusted peers right
        // away keeps them able to reach it, and shows which of them are reachable.
        public void HandleAsync(PeerTransportStateChangedEvent message)
        {
            if (!_transport.IsRunning)
            {
                return;
            }

            foreach (var peer in _peers.All().Where(p => p.State == PeerState.Trusted))
            {
                Ping(peer.Id);
            }
        }

        public NodeIdentity GetIdentity()
        {
            if (!_transport.IsRunning)
            {
                return new NodeIdentity { Name = LocalName };
            }

            var address = _transport.GetLocalAddress();

            return new NodeIdentity
            {
                TransportRunning = true,
                NodeId = address.NodeId,
                Name = LocalName,
                RelayUrl = address.RelayUrl,
                IpAddresses = address.IpAddresses
            };
        }

        public List<Peer> All()
        {
            return _peers.All().OrderBy(p => p.Name).ToList();
        }

        public List<PeerInvite> Invites()
        {
            return _invites.All().OrderByDescending(i => i.Created).ToList();
        }

        public PeerReply Ask(Peer peer, PeerMessage message)
        {
            if (peer.State != PeerState.Trusted)
            {
                throw new PeerException("Only a trusted node can be contacted");
            }

            var local = _transport.GetLocalAddress();

            message.SenderName = LocalName;
            message.SenderRelayUrl = local.RelayUrl;
            message.SenderIpAddresses = local.IpAddresses;

            return Send(peer, message);
        }

        public CreatedInvite CreateInvite(TimeSpan validFor, string label, bool shareAll = false)
        {
            var address = _transport.GetLocalAddress();
            var now = DateTime.UtcNow;
            var secret = InviteCode.NewSecret();

            var invite = _invites.Insert(new PeerInvite
            {
                SecretHash = InviteCode.HashSecret(secret),
                Label = label,
                Created = now,
                Expires = now + validFor,
                ShareAll = shareAll
            });

            var code = new InviteCode
            {
                NodeId = address.NodeId,
                RelayUrl = address.RelayUrl,
                IpAddresses = address.IpAddresses,
                Secret = secret,
                ExpiresUnix = new DateTimeOffset(invite.Expires).ToUnixTimeSeconds()
            };

            return new CreatedInvite { Invite = invite, Code = code.Encode() };
        }

        public void DeleteInvite(int id)
        {
            _invites.Delete(id);
        }

        public Peer RedeemInvite(string code)
        {
            if (!InviteCode.TryDecode(code, out var invite))
            {
                throw new PeerException("That is not a Nodearr invite code");
            }

            var local = _transport.GetLocalAddress();

            if (invite.NodeId == local.NodeId)
            {
                throw new PeerException("This invite was created by this node");
            }

            if (invite.Expires <= DateTime.UtcNow)
            {
                throw new PeerException("This invite has expired");
            }

            var peer = _peers.FindByNodeId(invite.NodeId);

            if (peer is { State: PeerState.Blocked })
            {
                throw new PeerException("This node is blocked. Unblock it before pairing with it again");
            }

            var now = DateTime.UtcNow;

            // Trust that already exists must survive a bad or reused code, so only a node that is
            // not trusted yet is put in the waiting state.
            var alreadyTrusted = peer is { State: PeerState.Trusted };

            peer ??= new Peer { NodeId = invite.NodeId, Name = invite.NodeId[..12], Added = now };
            peer.SetAddress(invite.RelayUrl, invite.IpAddresses);

            if (!alreadyTrusted)
            {
                ChangeState(peer, PeerState.Pending, "Waiting for the other node to confirm");
            }

            var message = NewMessage(PeerMessageTypes.Pair, local);

            message.InviteSecret = invite.Secret;

            PeerReply reply;

            try
            {
                reply = Send(peer, message);
            }
            catch (TransportException ex)
            {
                if (!alreadyTrusted)
                {
                    peer.LastError = ex.Message;
                    SaveContact(peer);
                }

                throw new PeerException("Unable to reach the node that created this invite: " + ex.Message);
            }

            if (reply is not { Ok: true })
            {
                if (!alreadyTrusted)
                {
                    peer.LastError = "The other node did not accept the invite";
                    SaveContact(peer);
                }

                throw new PeerException("The other node did not accept this invite. It may have expired or already been used");
            }

            peer.Name = reply.Name.IsNotNullOrWhiteSpace() ? reply.Name : peer.Name;
            peer.LastSeen = DateTime.UtcNow;
            peer.LastError = null;

            return ChangeState(peer, PeerState.Trusted, null);
        }

        public Peer Ping(int id)
        {
            var peer = _peers.Get(id);

            if (peer.State != PeerState.Trusted)
            {
                throw new PeerException("Only a trusted node can be contacted");
            }

            try
            {
                var reply = Send(peer, NewMessage(PeerMessageTypes.Ping, _transport.GetLocalAddress()));

                if (reply is not { Ok: true })
                {
                    throw new TransportException(TransportFailure.Failed, "The node answered with an error");
                }

                peer.Name = reply.Name.IsNotNullOrWhiteSpace() ? reply.Name : peer.Name;
                peer.LastSeen = DateTime.UtcNow;
                peer.LastError = null;
            }
            catch (TransportException ex)
            {
                peer.LastError = $"{ex.Failure}: {ex.Message}";
            }

            return SaveContact(peer);
        }

        public Peer Block(int id)
        {
            var peer = ChangeState(_peers.Get(id), PeerState.Blocked, "Blocked here");

            PushBlocked();

            return peer;
        }

        public Peer Unblock(int id)
        {
            var peer = _peers.Get(id);

            if (peer.State != PeerState.Blocked)
            {
                return peer;
            }

            // Unblocking does not restore trust. Pairing again takes a new invite.
            peer = ChangeState(peer, PeerState.Revoked, "Unblocked. Pair again to trust this node");
            PushBlocked();

            return peer;
        }

        public Peer Revoke(int id)
        {
            var peer = _peers.Get(id);

            if (peer.State != PeerState.Trusted)
            {
                throw new PeerException("Only a trusted node can be revoked");
            }

            try
            {
                // A courtesy, so the other side shows the right state. Revocation does not depend on it.
                Send(peer, NewMessage(PeerMessageTypes.Revoke, _transport.GetLocalAddress()));
            }
            catch (TransportException ex)
            {
                _logger.Debug(ex, "Unable to tell {0} that it was revoked", peer.Name);
            }

            return ChangeState(peer, PeerState.Revoked, "Revoked here");
        }

        public void Delete(int id)
        {
            var peer = _peers.Get(id);

            if (peer.State == PeerState.Blocked)
            {
                throw new PeerException("Deleting a blocked node would forget the block. Unblock it first");
            }

            if (peer.State == PeerState.Trusted)
            {
                Revoke(id);
            }

            _peers.Delete(id);
            _eventAggregator.PublishEvent(new PeerDeletedEvent(id));
        }

        // Every message from another node arrives here. Returning null refuses it.
        public byte[] HandleIncoming(string remoteNodeId, byte[] body)
        {
            var peer = _peers.FindByNodeId(remoteNodeId);

            // The transport already turns blocked nodes away. This is the second lock on the same door.
            if (peer is { State: PeerState.Blocked })
            {
                return null;
            }

            var message = PeerMessage.FromBytes(body);

            if (message is not { Version: 1 })
            {
                return null;
            }

            switch (message.Type)
            {
                case PeerMessageTypes.Pair:
                    return Pair(remoteNodeId, peer, message).ToBytes();

                // Everything except pairing needs trust that already exists.
                case PeerMessageTypes.Ping when peer is { State: PeerState.Trusted }:
                    peer.SetAddress(message.SenderRelayUrl, message.SenderIpAddresses);
                    peer.LastSeen = DateTime.UtcNow;
                    SaveContact(peer);

                    return new PeerReply { Ok = true, Name = LocalName }.ToBytes();

                // What this node is told is decided by the sharing rules, for this peer only.
                case PeerMessageTypes.Catalog when peer is { State: PeerState.Trusted }:
                    return _offerExchange.Answer(peer, message).ToBytes();

                // Only a hint. This node fetches the catalog itself, when it gets to it.
                case PeerMessageTypes.CatalogChanged when peer is { State: PeerState.Trusted }:
                    _eventAggregator.PublishEvent(new PeerCatalogChangedEvent(peer.Id));

                    return new PeerReply { Ok = true }.ToBytes();

                case PeerMessageTypes.Revoke when peer is { State: PeerState.Trusted }:
                    ChangeState(peer, PeerState.Revoked, "Revoked by the other node");

                    return new PeerReply { Ok = true }.ToBytes();

                default:
                    return null;
            }
        }

        private PeerReply Pair(string remoteNodeId, Peer peer, PeerMessage message)
        {
            var invite = _invites.FindBySecretHash(InviteCode.HashSecret(message.InviteSecret));

            // One answer for every kind of bad invite, so a stranger learns nothing by guessing.
            if (invite == null || invite.RedeemedAt.HasValue || invite.Expires <= DateTime.UtcNow)
            {
                _logger.Warn("Node {0} tried to pair with an invite that is unknown, used or expired", remoteNodeId);

                return new PeerReply { Ok = false, Error = "InviteInvalid" };
            }

            var now = DateTime.UtcNow;

            invite.RedeemedAt = now;
            invite.RedeemedByNodeId = remoteNodeId;
            _invites.Update(invite);

            peer ??= new Peer { NodeId = remoteNodeId, Added = now };
            peer.Name = message.SenderName.IsNotNullOrWhiteSpace() ? message.SenderName : remoteNodeId[..12];
            peer.SetAddress(message.SenderRelayUrl, message.SenderIpAddresses);
            peer.LastSeen = now;
            peer.LastError = null;

            // The inviter chose this when creating the invite. Otherwise a new peer is offered nothing.
            peer.ShareAll = invite.ShareAll;
            ChangeState(peer, PeerState.Trusted, null);
            _peers.SetFields(peer, p => p.ShareAll);

            _logger.Info("Paired with {0} ({1})", peer.Name, remoteNodeId);

            return new PeerReply { Ok = true, Name = LocalName };
        }

        private PeerMessage NewMessage(string type, NodeAddress local)
        {
            return new PeerMessage
            {
                Type = type,
                SenderName = LocalName,
                SenderRelayUrl = local.RelayUrl,
                SenderIpAddresses = local.IpAddresses
            };
        }

        private PeerReply Send(Peer peer, PeerMessage message)
        {
            return PeerReply.FromBytes(_transport.Request(peer.ToAddress(), message.ToBytes(), RequestTimeout));
        }

        private Peer ChangeState(Peer peer, PeerState state, string reason)
        {
            var previous = peer.State;
            var isNew = peer.Id == 0;

            peer.State = state;
            peer.StateReason = reason;
            peer.StateChanged = DateTime.UtcNow;

            if (isNew)
            {
                peer = _peers.Insert(peer);
            }
            else
            {
                _peers.SetFields(peer, p => p.State, p => p.StateReason, p => p.StateChanged);
                SaveContact(peer);
            }

            if (isNew || previous != state)
            {
                _eventAggregator.PublishEvent(new PeerStateChangedEvent(peer, previous));
            }

            return peer;
        }

        // Writes only the fields this service owns. A whole-row update from here could undo a sharing
        // or catalog change made elsewhere while a message was in flight.
        private Peer SaveContact(Peer peer)
        {
            _peers.SetFields(peer, p => p.Name, p => p.RelayUrl, p => p.IpAddresses, p => p.LastSeen, p => p.LastError);

            return peer;
        }

        private void PushBlocked()
        {
            _transport.SetBlocked(_peers.All().Where(p => p.State == PeerState.Blocked).Select(p => p.NodeId));
        }
    }
}

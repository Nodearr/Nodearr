# ADR 0013: Node Identity, Pairing and Trust

## Status

Accepted. Built and checked in S4 on 2026-10-10.

## Context

Nodearr only exchanges catalogs and files with nodes its owner has chosen to trust. S4 had to provide a stable identity for each node, a way for two owners to pair their nodes without typing keys at each other, the trust states pending, trusted, blocked and revoked, and a block that takes effect before a blocked node can say anything.

The V1 plan had already decided that the Iroh Ed25519 key is the node identity and lives in a file (decision 5), that a Rust sidecar holds the transport (decision 4), and that tests use an in-memory fake (decision 6).

## Decision

### Identity

A node's identity is its Iroh endpoint key. The public key, 64 hexadecimal characters, is the node ID.

The secret key is created on first start by the `nodearr-transport` sidecar and stored in `node.key` in the data folder, not in the database. Nodearr itself never reads it. Backing up or moving a node means keeping that file. Deleting it makes the node a stranger to every peer.

Amendment, 2026-10-15: System > Backup includes `node.key` in every backup, with the settings and the database. Restoring one leaves the key as `node.key.restore`, and the transport puts it in place the next time it starts, which restoring always ends in. The key never changes under a running transport. A backup is therefore as secret as the key: whoever has one can be this node.

### The handshake is Iroh's

Iroh connects to a node by its public key and both ends prove possession of their keys during the QUIC handshake. The sidecar passes the proven remote node ID to Nodearr with every incoming message.

Nodearr therefore adds no signatures of its own. A message never states who sent it. The design overview's "exchange signed identity material" step is that handshake.

### Pairing with invites

Pairing uses a one-time invite.

1. The inviting node creates a random 256-bit secret, stores only its SHA-256 hash with an expiry time, and shows an invite code once. The code holds the inviter's node ID, where to reach it (relay URL and current addresses), the secret and the expiry.
2. The other owner pastes the code into their node. That node connects to the inviter's node ID and sends a pair message with the secret, its own display name and where it can be reached.
3. The inviter checks that the secret matches an invite that has not expired and has not been used. It marks the invite used, records the sender as trusted and answers with its own name.
4. The redeeming node records the inviter as trusted.

The invite proves the redeemer was invited. The handshake proves each node is the one the other recorded. Every refusal gives the same answer, so guessing reveals nothing about which invites exist.

Invites last between 1 hour and 7 days. The default is 24 hours.

### Trust states

| State | Meaning | What the node may do |
| --- | --- | --- |
| Pending | An invite was redeemed here and the other node has not confirmed yet. | Pair |
| Trusted | Both sides completed pairing. | Everything |
| Revoked | Trust was withdrawn, on either side. | Pair again with a new invite |
| Blocked | The owner refuses this node. | Nothing |

- **Revoke** ends trust. The node tells its peer as a courtesy, so the peer shows the right state, but revocation does not depend on the peer hearing it.
- **Block** is silent and durable. The peer is not told. A blocked node cannot pair again even with a valid invite, and its record cannot be deleted, because deleting it would forget the block.
- **Unblock** moves the node to Revoked, not back to Trusted. Trust always takes a new invite.
- A reused or invalid invite does not disturb trust that already exists.

### Where rules are enforced

- The sidecar holds the block list and closes a connection from a blocked node as soon as it is accepted, before reading from it. Nodearr gives it the list at start and after every change.
- Nodearr checks again for every message: blocked nodes get no answer, only a pair message is accepted from a node that is not trusted, and everything else requires trust.
- A state change raises `PeerStateChangedEvent`. The slices that cache peer catalogs (S5) and run transfers (S7) subscribe to it to purge and cancel.

### The sidecar boundary

Nodearr starts `nodearr-transport`, restarts it if it stops, and stops it on shutdown. The sidecar also exits by itself when Nodearr's end of its standard input closes, so it cannot outlive Nodearr.

They talk over HTTP on a loopback port the sidecar picks. Every request carries a random token that Nodearr generates at each start and passes to the sidecar in an environment variable, never on the command line. All requests go from Nodearr to the sidecar. Incoming peer messages are collected by a long poll, so Nodearr's own web port is not involved.

The sidecar carries opaque messages. What they mean is decided in Nodearr, behind the `IPeerTransport` interface, which has two implementations: the sidecar, and an in-memory network used by the unit tests.

### Addresses

There is no lookup service. Nodes learn each other's relay URL and addresses from the invite and from each other: every message carries the sender's current addresses, and a node tells its trusted peers when its transport starts.

## Evidence

- 26 unit tests drive three nodes over the in-memory network: pairing, one-time and expired invites, forged secrets, strangers, blocking at the transport and again in Nodearr, unblocking, revoking with the peer offline, and the block list surviving a transport restart.
- `acceptance/Run-S4Pairing.ps1` runs two real Nodearr instances with real sidecars on one machine. 11 of 11 checks pass, including that a restart keeps the identity, leaves no orphan sidecar, and that the peer finds the restarted node again.

## Consequences

### Positive

- No key handling or signature code in Nodearr. The identity proof is the transport's.
- A block is enforced at the earliest possible point and survives restarts of either process.
- The same peer logic runs against the fake and the real transport.

### Negative / trade-offs

- With relays disabled, a node's addresses change on every restart, and a peer that was offline at that moment has stale ones until the two meet again. With a relay the relay URL stays valid, which is one more reason coordinated mode is the default (ADR 0011).
- There is no lookup service, so two nodes that both lose track of each other must pair again. n0's DNS-based lookup would fix that at the cost of publishing node addresses to a third party. Not decided.
- The node's display name is the machine name for now. There is no setting yet.
- ~~Pair attempts are not rate limited.~~ They are now: see the amendment of 2026-10-15.
- The acceptance run used one machine with relays off. Pairing across the internet depends on the same connectivity that S2's cross-NAT run has yet to confirm.

## Follow-up work

- S5: purge a peer's cached catalog on block and revoke.
- S7: cancel a peer's transfers on block and revoke, and apply the same authorization to every transfer request.
- S8: ship the sidecar in the Docker image, and settings for relay mode, relay URL and port mapping (today they are `Nodearr__Transport__*` environment variables).
- A setting for the node's display name.

## Amendments

### 2026-10-15: pair attempts are limited

A stranger who knows a node's ID and can reach it could try invite secrets without end. The secret is 256 bits, so nothing can be guessed, but each attempt cost a lookup and a warning in the log, and node IDs cost nothing to make.

- A node that fails to pair five times in 15 minutes is turned away for the rest of that time, without its invite being looked at, even when it brings a real one. It is told `TooManyAttempts`, which says nothing about which invites exist, and the node redeeming the invite says to try again in 15 minutes. A node that pairs starts again from nothing.
- Pairing is never closed to everyone. A limit for all nodes at once would let one stranger keep a friend from pairing.
- The first ten failures in 15 minutes are logged one by one. One more warning says the rest are only counted, and they are logged at debug level.
- System > Status warns when a node has been turned away, or after 20 failures in an hour, and says how many and from how many node IDs.
- Everything is kept in memory, at most 10,000 node IDs. A restart forgets it, which costs nothing.

# Peer invite and trust establishment

A short-lived, one-time invite authorizes pairing. The nodes' long-lived keys establish who they are from then on. This is the flow as built in S4. See [ADR 0013](../adr/0013-node-identity-pairing-and-trust.md).

## Pairing

```mermaid
sequenceDiagram
    autonumber
    actor Owner as John
    participant NodeA as John's Nodearr
    participant Relay as Relay (coordination only)
    participant NodeB as Ryan's Nodearr
    actor Peer as Ryan

    Owner->>NodeA: Create invite, valid 24 hours
    NodeA->>NodeA: Make a random secret, store only its hash and the expiry
    NodeA-->>Owner: Invite code: node ID, relay URL, addresses, secret, expiry
    Owner-->>Peer: Send the code out of band
    Peer->>NodeB: Paste the code
    NodeB->>NodeB: Record John's node as Pending
    NodeB->>Relay: Connect to John's node ID
    Relay->>NodeA: Coordinate the connection
    Note over NodeA,NodeB: QUIC handshake: each node proves it holds the key for its node ID
    NodeB->>NodeA: pair: secret, Ryan's name and addresses
    NodeA->>NodeA: Secret matches an unused, unexpired invite
    NodeA->>NodeA: Mark the invite used, record Ryan's node as Trusted
    NodeA-->>NodeB: ok, John's name
    NodeB->>NodeB: Record John's node as Trusted
```

The relay helps the two nodes find a path to each other. It cannot read the messages and never sees the invite secret.

Every refusal says the same, `InviteInvalid`, whatever was wrong with the invite, so a stranger learns nothing by guessing. A node that has failed five times in 15 minutes is answered `TooManyAttempts` instead, without its invite being looked at, until those failures are 15 minutes old ([ADR 0013](../adr/0013-node-identity-pairing-and-trust.md), amendments).

## Trust states

```mermaid
stateDiagram-v2
    [*] --> Pending: redeem an invite
    Pending --> Trusted: the inviter accepts
    [*] --> Trusted: a node redeems this node's invite
    Trusted --> Revoked: revoke, here or by the peer
    Revoked --> Trusted: pair again with a new invite
    Trusted --> Blocked: block
    Pending --> Blocked: block
    Revoked --> Blocked: block
    Blocked --> Revoked: unblock
```

## A blocked node

```mermaid
sequenceDiagram
    autonumber
    participant NodeB as Blocked node
    participant Sidecar as John's transport sidecar
    participant NodeA as John's Nodearr

    NodeA->>Sidecar: Block list, at start and on every change
    NodeB->>Sidecar: Connect
    Note over NodeB,Sidecar: The handshake proves which node this is
    Sidecar--xNodeB: Close the connection: Blocked
    Note over Sidecar,NodeA: Nothing reaches Nodearr
```

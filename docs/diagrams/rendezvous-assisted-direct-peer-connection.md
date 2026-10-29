# How two nodes reach each other

Each node's sidecar keeps a connection open to its relay. The relay tells it its public address, passes on the first messages between two nodes, and helps them make a direct connection by both sending at once. Files then move only over that direct connection. See [ADR 0011](../adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md).

```mermaid
sequenceDiagram
    autonumber
    participant A as John's sidecar
    participant Relay as Relay (n0's, or your own)
    participant B as Ryan's sidecar

    Note over A,B: Before anything: each keeps a connection to its relay
    A->>Relay: Connected, as John's node ID
    Relay-->>A: Your public address is ...
    B->>Relay: Connected, as Ryan's node ID
    Relay-->>B: Your public address is ...

    Note over A,B: John's node wants Ryan's (pairing, a catalog, a file)
    A->>Relay: For Ryan's node ID: QUIC handshake
    Relay->>B: Forwarded, still encrypted
    B-->>Relay: Handshake reply
    Relay-->>A: Forwarded
    Note over A,B: Each proves it holds the key for its node ID. Pairing and connection checks can already flow, through the relay. Catalogs, posters and sent titles wait for a direct path.
    A->>Relay: My addresses: public and local
    Relay->>B: Forwarded
    B->>Relay: Mine
    Relay->>A: Forwarded
    par Both send at once ("hole punching")
        A->>B: UDP to Ryan's addresses
    and
        B->>A: UDP to John's addresses
    end
    alt Each router lets the other's packets in, as replies
        A-->>B: Direct path selected
        Note over A,B: Catalogs, posters, sent titles and files go directly from now on
    else No direct path
        Note over A,B: Pairing and checks keep going through the relay. Catalogs, posters, sent titles and files are never sent.
    end
```

- The relay sees two node IDs, their addresses, and when they talk. It cannot read what passes through it, and nothing of what a library holds passes through it ([ADR 0011, amendment of 2026-10-29](../adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md#amendment-2026-10-29-what-may-cross-a-relay)).
- A sidecar also asks the router to open its port (UPnP, NAT-PMP or PCP) unless that is turned off, which makes a direct path likelier.
- With no relay (**Settings > Node**), nodes can only use addresses they already know for each other, such as on the same network or a forwarded port.

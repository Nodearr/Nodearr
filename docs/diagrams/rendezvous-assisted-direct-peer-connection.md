# Rendezvous-assisted direct peer connection

Rendezvous helps trusted peers find each other and exchange connection hints. The media transfer path is a direct encrypted peer connection, not an official Nodearr relay.

```mermaid
sequenceDiagram
    autonumber
    participant NodeA as John Nodearr
    participant Rendezvous as Rendezvous Service
    participant NodeB as Ryan Nodearr

    NodeA->>Rendezvous: Publish node ID and observed address hints
    NodeB->>Rendezvous: Publish node ID and observed address hints
    NodeA->>Rendezvous: Request session hints for Ryan
    Rendezvous-->>NodeA: Ryan hints and temporary session metadata
    Rendezvous-->>NodeB: John hints and temporary session metadata
    par Simultaneous outbound attempt
        NodeA->>NodeB: Dial encrypted QUIC connection
    and Simultaneous outbound attempt
        NodeB->>NodeA: Dial encrypted QUIC connection
    end
    alt Direct path established
        NodeA-->>NodeB: Authenticated direct peer session
        NodeB-->>NodeA: Authenticated direct peer session
    else Direct path not established
        NodeA->>NodeA: Report connection failure to transfer job
    end

    Note over Rendezvous: Rendezvous carries connection metadata only, not media bytes.
```

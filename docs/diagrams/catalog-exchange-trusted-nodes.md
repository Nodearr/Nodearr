# Catalog exchange between trusted nodes

A trusted node asks another for its catalog and is answered with the offer built for it alone. No media bytes move, and nothing about where files are stored is sent. Built in S5, and since 2026-10-14 only what changed is sent after the first time. See [ADR 0014](../adr/0014-sharing-rules-and-catalog-exchange.md).

## Fetching a peer's catalog

```mermaid
sequenceDiagram
    autonumber
    participant NodeB as Ryan's Nodearr
    participant NodeA as John's Nodearr
    participant Rules as John's sharing rules
    participant Local as Ryan's local catalog

    Note over NodeB,NodeA: The connection handshake has already proved who each node is
    NodeB->>NodeA: catalog, page 0, "I hold version 4f2a, and can take changes and albums"
    NodeA->>NodeA: Is Ryan trusted? If not, refuse
    NodeA->>Rules: Build Ryan's offer
    Rules-->>NodeA: Not hidden, and shared with Ryan or with everyone
    alt Nothing changed
        NodeA-->>NodeB: unchanged, version 4f2a
    else John knows what Ryan holds
        NodeA-->>NodeB: version 9c1e: these files added or changed, these IDs removed
        NodeB->>NodeB: Apply them. Do they add up to version 9c1e? If not, ask for the whole catalog
    else The first time, or more than half changed
        NodeA-->>NodeB: version 9c1e, 3 pages, first 500 files
        NodeB->>NodeA: catalog, page 1 of version 9c1e
        NodeA-->>NodeB: next 500 files, from the same snapshot
        NodeB->>NodeA: catalog, page 2 of version 9c1e
        NodeA-->>NodeB: the rest
        NodeB->>NodeB: Replace everything held for John with version 9c1e
    end
    NodeB->>Local: Mark each file: not in library, missing, upgrade or have
```

Each file carries a fingerprint of what is said about it, and the version is worked out from the fingerprints, so a node can check that what it holds is what it was offered. A node that does not say it can take albums, an older one, is offered none.

## When something is taken back

```mermaid
sequenceDiagram
    autonumber
    actor Owner as John
    participant NodeA as John's Nodearr
    participant NodeB as Ryan's Nodearr

    Owner->>NodeA: Hide a series, take it off a list, or delete it in Sonarr
    NodeA->>NodeB: catalogChanged (a hint, with no content)
    NodeB->>NodeA: catalog, page 0
    NodeA-->>NodeB: New version, without the hidden series
    NodeB->>NodeB: Replace John's catalog. The series is gone.
```

Only taking something back is announced. What is newly shared waits for each friend's next fetch: every Catalog Sync Interval, 15 minutes by default, or when they press **Sync Peer Catalogs**. A missed hint likewise only delays the update until then.

## When trust ends

```mermaid
sequenceDiagram
    autonumber
    actor Owner as John
    participant NodeA as John's Nodearr
    participant NodeB as Ryan's Nodearr

    Owner->>NodeA: Revoke Ryan
    NodeA->>NodeB: revoke
    NodeB->>NodeB: Mark John as revoked, delete John's catalog
    NodeA->>NodeA: Delete Ryan's catalog
    NodeB->>NodeA: catalog (a later attempt)
    NodeA--xNodeB: Refused
```

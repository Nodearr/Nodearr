# Catalog exchange between trusted nodes

Trusted Nodearr instances exchange normalized availability metadata. Catalog exchange does not transfer media bytes and should not require peers to reveal unrelated private state.

```mermaid
sequenceDiagram
    autonumber
    participant NodeA as John Nodearr
    participant TrustA as John Trust Store
    participant NodeB as Ryan Nodearr
    participant TrustB as Ryan Trust Store
    participant CatalogA as John Catalog
    participant CatalogB as Ryan Catalog

    NodeA->>TrustA: Load Ryan trusted public key
    NodeA->>NodeB: Request catalog delta since last sync
    NodeB->>TrustB: Verify John trusted public key
    TrustB-->>NodeB: Trust accepted
    NodeB->>CatalogB: Read shareable availability records
    CatalogB-->>NodeB: Normalized candidate metadata
    NodeB-->>NodeA: Signed catalog delta
    NodeA->>TrustA: Verify Ryan signature
    TrustA-->>NodeA: Signature accepted
    NodeA->>CatalogA: Store peer availability records
    CatalogA-->>NodeA: Updated peer catalog view
```
# Arr search/grab flow where Nodearr acts as indexer

Nodearr exposes peer availability as Torznab/Newznab-style search results. A grab URL creates a Nodearr transfer job; 
the indexer API itself does not move file bytes.

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Sonarr or Radarr
    participant Prowlarr as Prowlarr optional
    participant Indexer as Nodearr Indexer API
    participant Catalog as Peer Catalog
    participant Matcher as Profile-Aware Matcher
    participant Jobs as Transfer Job Store

    alt Arr searches through Prowlarr
        Arr->>Prowlarr: Search wanted media
        Prowlarr->>Indexer: Torznab/Newznab search
        Indexer->>Catalog: Find trusted peer candidates
        Catalog-->>Indexer: Peer availability records
        Indexer->>Matcher: Apply media and profile matching
        Matcher-->>Indexer: Acceptable candidates
        Indexer-->>Prowlarr: Torznab/Newznab results
        Prowlarr-->>Arr: Search results
        Arr->>Prowlarr: Grab selected result
        Prowlarr->>Indexer: Request Nodearr grab URL
        Indexer->>Jobs: Create transfer job for selected candidate
        Jobs-->>Indexer: Job ID and download-client-visible state
        Indexer-->>Prowlarr: Grab accepted
        Prowlarr-->>Arr: Grab accepted
    else Arr searches Nodearr directly
        Arr->>Indexer: Torznab/Newznab search
        Indexer->>Catalog: Find trusted peer candidates
        Catalog-->>Indexer: Peer availability records
        Indexer->>Matcher: Apply media and profile matching
        Matcher-->>Indexer: Acceptable candidates
        Indexer-->>Arr: Torznab/Newznab results
        Arr->>Indexer: Request Nodearr grab URL
        Indexer->>Jobs: Create transfer job for selected candidate
        Jobs-->>Indexer: Job ID and download-client-visible state
        Indexer-->>Arr: Grab accepted
    end
```
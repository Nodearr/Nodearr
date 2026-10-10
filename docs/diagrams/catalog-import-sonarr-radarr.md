# Catalog import from Sonarr/Radarr

Nodearr imports catalog data from configured Arr applications. Sonarr and Radarr remain the source of truth for media identity, library membership, paths, quality/profile data, and import workflow state.

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant UI as Nodearr UI
    participant Nodearr
    participant Arr as Sonarr or Radarr
    participant Store as Catalog Store

    User->>UI: Add Arr app URL and API key
    UI->>Nodearr: Save Arr app connection
    Nodearr->>Arr: Test API key and app capabilities
    Arr-->>Nodearr: App status and supported resources
    Nodearr->>Arr: Request library items and file metadata
    Arr-->>Nodearr: Media identity, paths, qualities, profiles
    Nodearr->>Nodearr: Normalize into Nodearr catalog entries
    Nodearr->>Store: Upsert local availability records
    Store-->>Nodearr: Sync result
    Nodearr-->>UI: Show catalog import status
```


# Direct transfer flow where Nodearr acts as download client

After a grab, Nodearr owns the transfer job. It retrieves bytes directly from the trusted source peer, verifies the completed file, and exposes job state so the Arr app can import normally.

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Sonarr or Radarr
    participant Local as John Nodearr
    participant Provider as Transfer Provider
    participant Peer as Ryan Nodearr
    participant Store as Transfer Job Store
    participant Files as Completed Download Path

    Local->>Store: Load queued transfer job
    Arr->>Local: Poll download-client queue/status
    Local-->>Arr: Job queued
    Local->>Provider: Connect to trusted peer for candidate
    Provider->>Peer: Establish direct encrypted session
    Peer->>Peer: Authorize John and candidate request
    Peer-->>Provider: Authorization accepted
    Provider->>Peer: Request file bytes
    Peer-->>Provider: Stream media bytes directly
    Provider-->>Local: Bytes received
    Local->>Local: Verify size and content hash
    Local->>Files: Write verified completed file
    Local->>Store: Mark transfer completed
    Arr->>Local: Poll download-client queue/status
    Local-->>Arr: Completed path is import-ready
    Arr->>Files: Import using normal Arr workflow
```

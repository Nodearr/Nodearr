# Failure flow when direct connectivity cannot be established

If trusted peers cannot establish a direct connection, official Nodearr infrastructure does not relay media bytes. The transfer fails clearly unless the user has explicitly configured their own alternate network path.

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Sonarr or Radarr
    participant Local as John Nodearr
    participant Rendezvous as Rendezvous Service
    participant Peer as Ryan Nodearr
    participant Policy as Nodearr Relay Policy
    participant Store as Transfer Job Store

    Local->>Store: Start transfer job
    Local->>Rendezvous: Request peer connection hints
    Rendezvous-->>Local: Temporary hints for Ryan
    Rendezvous-->>Peer: Temporary hints for John
    par Direct connection attempts
        Local-xPeer: Direct encrypted dial fails
    and Direct connection attempts
        Peer-xLocal: Direct encrypted dial fails
    end
    Local->>Policy: Evaluate fallback options
    Policy-->>Local: Official media-byte relay is not allowed
    Local->>Store: Mark job failed with clear connectivity reason
    Arr->>Local: Poll download-client queue/status
    Local-->>Arr: Transfer failed - no official relay fallback
    
    
    Note over Rendezvous: Rendezvous may help peers find each other, but it does not relay media bytes.
```

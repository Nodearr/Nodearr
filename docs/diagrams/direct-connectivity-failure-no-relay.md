# When there is no direct path

If two nodes cannot reach each other directly, they can still talk through the relay, but no file moves: the transfer fails and says why. See [ADR 0011](../adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md) and [ADR 0016](../adr/0016-transfers-and-the-download-client.md).

## No direct path from the start

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Ryan's Sonarr
    participant Local as Ryan's Nodearr
    participant Side as Ryan's sidecar
    participant Relay
    participant PeerSide as John's sidecar

    Arr->>Local: addfile, with the ticket
    Local->>Side: Start the transfer
    Side->>Relay: Connect to John's node
    Relay->>PeerSide: Forwarded
    Side->>PeerSide: Request the file (through the relay)
    PeerSide->>PeerSide: Allowed? Hash the file. Wait for a direct path, up to 20 seconds
    Note over Side,PeerSide: Hole punching fails: a firewall drops the packets, or a router changes the port per destination
    PeerSide--xSide: NoDirectRoute. Not one byte of the file was sent.
    Side-->>Local: Failed: NoDirectRoute
    Local->>Local: Up to three attempts total, 15 seconds between retries
    Arr->>Local: history
    Local-->>Arr: Failed: "Nodearr transfer failed: NoDirectRoute. The two nodes could not reach each other directly, and Nodearr never sends files through a relay." and what is known of the route
    Arr->>Arr: Record it, blocklist the release
```

## The direct path goes away part way

```mermaid
sequenceDiagram
    autonumber
    participant Side as Ryan's sidecar
    participant PeerSide as John's sidecar

    PeerSide-->>Side: File data, over the direct path
    Note over Side,PeerSide: The direct path is lost. iroh would fall back to the relay.
    PeerSide->>PeerSide: The selected path is no longer direct: reset the stream at once
    PeerSide--xSide: DirectPathLost. Reset the stream and close the connection.
    Side->>Side: Keep what arrived, and refuse anything that comes another way
    Note over Side,PeerSide: The next attempt carries on from what arrived, once a direct path is back
```

**Nodes > Trusted Nodes** shows such a friend as **Relay only** after a connection check, and **System > Status** names them, with what to try: the [troubleshooting guide](../user-guide/troubleshooting.md#relay-only) goes through it.

The reset is built; counting relay bytes when a direct path is lost part way is still open in [ADR 0011](../adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md#follow-up-work).

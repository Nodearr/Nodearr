# Transfer flow, with Nodearr as the download client

After a grab, Nodearr owns the transfer. It fetches the file directly from the trusted node that shares it, checks it against the hash that node gave first, and only then puts it where the Arr app looks. This is the flow as built in S7. See [ADR 0016](../adr/0016-transfers-and-the-download-client.md).

## A transfer that works

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Ryan's Sonarr
    participant Local as Ryan's Nodearr
    participant Side as Ryan's sidecar
    participant PeerSide as John's sidecar
    participant Peer as John's Nodearr
    participant Disk as Ryan's download folder

    Arr->>Local: addfile, with the ticket
    Local->>Local: Read the candidate ID. Is it still offered? Queue a transfer.
    Local-->>Arr: nzo_id
    Local->>Side: Start: John's address, John's ID for the file, incomplete path
    Side->>PeerSide: Connect, ask for the file
    PeerSide->>Peer: May this node have this file?
    Peer->>Peer: Trusted now? Offered this file now? Readable here?
    Peer-->>PeerSide: Yes, and where it is on disk
    PeerSide->>PeerSide: Size and BLAKE3 hash, fixed before any data moves
    PeerSide-->>Side: Header: size and hash
    loop Only while the selected path is direct
        PeerSide-->>Side: File bytes
        Side->>Disk: Write under incomplete, hashing as it goes
    end
    Arr->>Local: queue
    Local-->>Arr: Downloading, with how much is left
    Side->>Side: Size and hash match
    Side-->>Local: Completed
    Local->>Disk: Move the folder from incomplete to complete, in one step
    Arr->>Local: history
    Local-->>Arr: Completed, with the folder
    Arr->>Disk: Import
    Arr->>Local: Delete from history, with files
```

## Restarting in the middle

```mermaid
sequenceDiagram
    autonumber
    participant Local as Ryan's Nodearr
    participant Side as Ryan's sidecar
    participant PeerSide as John's sidecar
    participant Peer as John's Nodearr

    Note over Local,Side: Ryan's Nodearr stops. 6 MB of 21 MB is under incomplete.
    Local->>Local: Starts again. The transfer is in the database, with the hash it was told.
    Local->>Side: Start again: same file, same path, that hash
    Side->>PeerSide: I hold 6 MB of the file with this hash
    PeerSide->>Peer: May this node have this file?
    Peer-->>PeerSide: Yes
    PeerSide-->>Side: Same hash. Carrying on from 6 MB.
    Side->>Side: Read the 6 MB back into the hash, then keep writing
    Side-->>Local: Completed, and the whole file matches
```

If John's file had changed, the hash would differ and the sender would start from the beginning.

## When it fails

```mermaid
sequenceDiagram
    autonumber
    actor John
    participant Arr as Ryan's Sonarr
    participant Local as Ryan's Nodearr
    participant Peer as John's Nodearr

    Note over Local,Peer: A transfer is under way
    John->>Peer: Revoke Ryan
    Peer->>Peer: Cut every upload to Ryan
    Peer->>Local: revoke
    Local->>Local: Fail the transfer: Revoked. Delete what had arrived.
    Arr->>Local: history
    Local-->>Arr: Failed: "Nodearr transfer failed: Revoked. Trust between this node and the other node has ended."
    Arr->>Arr: Record the failure, blocklist the release
    Arr->>Local: Delete from history
```

The same shape holds for every failure in ADR 0016. Those that may pass, such as a lost connection, get up to three attempts total per source node, with 15 seconds between retries, carrying on from what had arrived.

## Carrying on from another friend

```mermaid
sequenceDiagram
    autonumber
    participant Local as Ryan's Nodearr
    participant John as John's Nodearr
    participant Ann as Ann's Nodearr

    Note over Local,John: 10 MB of 21 MB has arrived from John
    John--xLocal: John goes offline: PeerUnreachable
    Local->>Local: Does another trusted node offer a file with the same hash and size?
    Local->>Ann: Ann does. Ask for her copy: I hold 10 MB of the file with this hash
    Ann-->>Local: Same hash. Carrying on from 10 MB.
    Local->>Local: Completed, and the whole file matches
```

Each node is moved to once, and has its own three attempts. A node that is directly reachable is tried first. A file that did not match its hash is tried again from the same node first, since it is that node that must work its hash out again.

## An album

An album from Lidarr arrives as its tracks, one transfer after another into the same folder under `incomplete`, each checked against its own hash and each with its own attempts. The folder moves into `complete` only once the last track is checked, so Lidarr never sees half an album. An album stays with the node it came from. See [ADR 0020](../adr/0020-lidarr-albums-shared-and-moved-as-folders-of-tracks.md).

# Nodearr Product Definition

> Written before Nodearr was built, as the definition it started from. Some of it was built differently or not at all: there was never a local HTTP transfer provider, and Readarr is not supported. What Nodearr is now is in the [overview](../overview-end-to-end-design.md) and the [user guide](../user-guide/README.md).

## Product one-liner

Nodearr is a self-hosted, Servarr-compatible, trusted peer-to-peer media availability and transfer service.

## Product intent

Nodearr lets a private group of trusted users discover and retrieve media files from each other through the automation flow Arr users already understand.

The core sentence is:

**Arr apps define what the media is. Nodearr defines who has an acceptable candidate and how to retrieve it.**

Expected flow:

1. A user runs Sonarr and/or Radarr.
2. Each trusted peer runs their own Nodearr instance.
3. Nodearr imports library metadata from configured Arr apps.
4. Nodearr normalizes imported data into local catalog entries.
5. Nodearr exchanges catalog availability with trusted peers.
6. Nodearr exposes peer-available candidates through a Torznab/Newznab-compatible indexer API.
7. A supported Arr app searches Nodearr directly or through Prowlarr.
8. Selecting a result creates a local Nodearr transfer job.
9. Nodearr retrieves verified bytes directly from a trusted peer.
10. The completed file is placed into a local completed-download path.
11. The user's existing Arr app imports it normally.

## V1 and future scope

V1 catalog sources:

- Sonarr.
- Radarr.

Future iterative catalog sources:

- Lidarr.
- Readarr.
- Whisparr / Eros.
- Other Arr-like applications that expose enough API metadata to support catalog import, candidate projection, and completed-download workflows.

Prowlarr is a downstream indexer consumer, not a catalog source. Media clients such as Jellyfin or Plex may consume the final imported library, but they are not Nodearr catalog sources.

## Non-goals for the first design pass

- Do not implement application features.
- Do not build a public indexer or public file-sharing network.
- Do not primarily scan folders and infer media identity.
- Do not treat content hashes as top-level media identity.
- Do not require VPN or manual port forwarding by default.
- Do not introduce official hosted media-byte relay.
- Do not treat Prowlarr as a catalog source.
- Do not treat media servers as catalog sources.

## Target users

### Primary user

A self-hosting Arr user who has trusted friends or family with overlapping media libraries and wants a private, automatable way to retrieve files from those peers.

### Secondary user

A small private group administrator who wants a clear view of peer health, catalog freshness, transfers, and configuration.

## User experience principles

- **Arr-compatible where it matters**: Arr apps should be able to search, grab, monitor, and import through familiar indexer and download-client conventions.
- **Arr-familiar product identity**: Nodearr should look and feel similar to existing Arr applications where that improves user confidence.
- **Trust-first**: every peer relationship is explicit and private.
- **Source-of-truth clarity**: Arr apps own media identity, library membership, paths, quality/profile data, and import workflows.
- **Profile-aware matching**: local policy decides whether a peer candidate is desirable.
- **Hash-verified transfer**: content hashes prove exact bytes and verification status.
- **Direct peer transfer**: Nodearr should move bytes directly between trusted nodes.
- **Inspectable operations**: users can see peers, catalog status, transfer progress, failures, logs, and health warnings.

## Core domain concepts

### Node

A Nodearr instance operated by a user. A node has a stable public identity, local configuration, connectivity state, and transfer capabilities.

### Peer

A remote trusted node from the local instance's perspective.

### Catalog

Nodearr's normalized view of local and peer availability.

The local catalog is imported from configured Arr applications. The peer catalog is received from trusted Nodearr peers. Catalog records describe media availability; they do not replace the source Arr application's media library.

### Media identity

The media identity imported from source Arr applications, such as movie IDs, series IDs, episode IDs, titles, years, season/episode numbers, quality/profile data, custom formats, language, and file paths.

### File identity

The exact-byte identity for a candidate file. It should include whole-file content hash, file size, hash algorithm/version, and verification state. Chunk hashes are future work for resumability or multi-source transfer.

### Release candidate

A Servarr-searchable projection of an available peer catalog item. It should include fields expected by Torznab/Newznab-like consumers: title, size, category, GUID, publish date where meaningful, download link, media IDs where available, and quality-like metadata.

### Transfer job

A local request to retrieve a selected release candidate. It tracks candidate identity, source peer, selected transfer provider, destination path, progress, state, verification results, errors, and retry/cancel history.

### Transfer provider

A pluggable backend for moving bytes from trusted peers to the local machine. Early milestones may use local HTTP or another simple transport. The leading real P2P direction is Iroh behind a Nodearr abstraction.

## Expected Nodearr surfaces

### Compatibility indexer API

A Torznab/Newznab-style indexer API sufficient for supported Arr apps and Prowlarr to search and grab release candidates.

Expected properties:

- API-key protected.
- Servarr-friendly categories and query parameters.
- Stable GUID/download links that map to Nodearr transfer jobs.
- Capability reporting.
- Search result metadata that preserves Nodearr file identity internally.

### Compatibility download-client API

An Arr-compatible surface for transfer job monitoring and completed-file handoff.

Expected properties:

- Queued and active transfer visibility.
- Progress and failure state.
- Completed path reporting.
- Remove/cancel support.
- Import-ready status.

### Native API

A Nodearr-owned versioned API for nodes, invites, catalog status, release candidates, transfer jobs, transfer providers, settings, health, tasks, logs, and system status.

### Web UI

An Arr-familiar local management UI with likely top-level areas:

- Catalog.
- Nodes.
- Activity / Transfers.
- Settings.
- System.

## Initial functional milestones

- **M1: Configure Arr app connections and import library metadata from Sonarr/Radarr.**
- **M2: Normalize imported Arr data into Nodearr catalog entries.**
- **M3: Add node identity and local peer pairing.**
- **M4: Sync catalogs between two Nodearr instances over local HTTP or another simple local transport.**
- **M5: Show local catalog, peer catalog, and missing/available comparisons in the UI.**
- **M6: Add profile-aware candidate matching.**
- **M7: Add content hashing for exact byte identity and verification.**
- **M8: Add transfer job model.**
- **M9: Add first direct file transfer provider.**
- **M10: Expose Nodearr as a Torznab/Newznab-compatible indexer.**
- **M11: Expose Nodearr as an Arr-compatible download client.**
- **M12: Replace local/LAN peer transport with real direct P2P discovery and transfer, likely using Iroh.**

## Product constraints

- Nodearr should not require users to replace Sonarr or Radarr.
- Nodearr should keep peer trust separate from local API/UI auth.
- Nodearr should land completed files in normal download/import paths.
- Nodearr should use Arr metadata for media identity and content hashes for exactness.
- Nodearr should keep Servarr compatibility at explicit API boundaries.
- Nodearr may use rendezvous/discovery infrastructure.
- Official Nodearr infrastructure must not relay media bytes.

## Success definition

Nodearr succeeds when a trusted group can run independent Nodearr instances, compare local and peer availability, search peer candidates from Arr-compatible tools, request a transfer, verify the retrieved bytes, and let existing Arr import automation complete the library workflow.

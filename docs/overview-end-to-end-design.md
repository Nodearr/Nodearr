# Nodearr overview: end-to-end design

## Summary

**Nodearr is a self-hosted, Servarr-compatible, trusted peer-to-peer media availability and transfer service.**

Nodearr runs alongside a user's existing Arr applications. It lets a small private group of trusted users discover who has acceptable media candidates and retrieve verified files directly from one another while keeping the existing Arr import workflow intact.

The key design sentence is:

**Arr apps define what the media is. Nodearr defines who has an acceptable candidate and how to retrieve it.**

V1 must support **Sonarr** and **Radarr** as catalog sources. Support for Lidarr, Readarr, Whisparr / Eros, and other Arr-like applications is planned iteratively and is not V1-required scope. See [Appendix A: Application support scope](#appendix-a-application-support-scope).

Nodearr has two major Arr-facing responsibilities:

1. **Indexer responsibility**: expose peer-available candidates through a Torznab/Newznab-compatible indexer API so Arr apps and indexer managers can search Nodearr like an indexer.
2. **Download-client responsibility**: accept grabbed candidates as transfer jobs, retrieve and verify bytes directly from trusted peers, and present job state and completed files in a way existing Arr apps can consume.

Nodearr is not a shared filesystem, shared RAID, shared Plex/Jellyfin service, public indexer, or generic library manager. Existing Arr apps remain the source of truth for media identity, library membership, file paths, quality/profile data, and import workflows.

## Primary goals

1. Connect trusted peers in a private decentralized transfer network securely, with minimal configuration and no manual port forwarding as the default expectation.
2. Index content from configured Arr app catalogs and share normalized availability metadata between connected peers.
3. Facilitate secure peer-to-peer downloads directly between connected Nodearr clients.
4. Make the user experience feel familiar to users of Sonarr, Radarr, Lidarr, and related Arr applications.
5. Provide sufficient documentation for users, contributors, and future implementation work.

## End-to-end user/system flow

1. John runs Nodearr next to his existing Sonarr and Radarr instances.
2. John configures Sonarr and Radarr API connections in Nodearr.
3. Nodearr imports library metadata from those Arr apps and normalizes it into local catalog entries.
4. George runs his own Nodearr instance and imports from his own Arr apps.
5. John creates a short-lived peer invite.
6. George redeems the invite and both Nodearr instances exchange signed node identity material.
7. Each Nodearr instance stores the other node's public identity as trusted.
8. Their nodes use discovery/rendezvous metadata to establish connectivity and exchange catalog availability.
9. John's Radarr searches for a wanted movie.
10. Radarr queries Nodearr as a Torznab/Newznab-compatible indexer, either directly or through Prowlarr as an indexer manager.
11. John's Nodearr searches trusted peer catalogs for candidates that match the requested media item and satisfy local policy.
12. Nodearr returns acceptable peer candidates as indexer search results with stable grab URLs.
13. Radarr grabs one result.
14. Nodearr creates a transfer job and behaves as the download client for that grabbed candidate.
15. John's Nodearr establishes a direct encrypted peer connection to George's Nodearr, likely using Iroh-assisted discovery and NAT traversal behind a Nodearr transfer abstraction.
16. George's Nodearr authorizes the request against peer trust and candidate availability.
17. George streams the file bytes directly to John. Official Nodearr infrastructure does not carry those media bytes.
18. John's Nodearr verifies the received bytes, writes the file to a completed-download path, and exposes completion state to the Arr app.
19. Radarr imports the file through its normal completed-download workflow.
20. The final library can then be consumed by whatever media client the user already uses, such as Jellyfin or Plex. Those clients are outside Nodearr's primary architecture.

## Core responsibilities

Nodearr owns these responsibilities:

- Import and normalize catalog data from configured Arr applications.
- Maintain local node identity and explicit trusted-peer relationships.
- Exchange availability metadata with trusted peers.
- Project peer availability into Torznab/Newznab-style indexer results.
- Accept grabs as transfer jobs and expose download-client-like job state.
- Retrieve bytes directly from trusted peers through pluggable transfer providers.
- Verify completed transfers with content hashes.
- Provide Arr-familiar UI/API surfaces for catalog, nodes, transfers, settings, health, logs, and status.

Nodearr does not own these responsibilities:

- Defining media identity from scratch when an Arr app already tracks it.
- Replacing Sonarr, Radarr, or other Arr applications.
- Treating Prowlarr as a catalog source.
- Scanning arbitrary folders as the primary way to infer media identity.
- Relaying media bytes through official hosted infrastructure.
- Serving media to end-user playback clients.

## Catalog responsibility

The catalog is the first major Nodearr domain.

Nodearr should not primarily scan folders and guess what media files represent. That duplicates the hardest and most product-specific parts of Sonarr, Radarr, and related applications.

Instead, Nodearr catalogs configured Arr instances. For V1, those catalog sources are Sonarr and Radarr.

For each configured Arr app, Nodearr imports known library files and metadata such as:

- Source app.
- Arr app item IDs.
- Media type.
- Movie, series, season, and episode identity as provided by the source app.
- External IDs where available.
- Title and year.
- Season and episode numbers where applicable.
- Quality, language, profile, and custom-format information where available.
- Codec, resolution, source, and related file attributes where available.
- File path.
- File size.
- Last seen / last synced timestamps.

Nodearr normalizes that imported data into its own catalog records.

The local catalog answers: **What does my local Arr stack say I have?**

The peer catalog answers: **What do trusted peers say they have?**

The comparison layer answers: **What do peers have that I am missing, can use, or may want as an upgrade?**

Prowlarr is downstream of Nodearr, not a catalog source. Prowlarr may configure Nodearr as an indexer so Arr apps can search Nodearr through an existing indexer manager.

## Indexer responsibility

Nodearr exposes a Torznab/Newznab-compatible indexer API.

That API lets Sonarr, Radarr, and indexer managers such as Prowlarr search Nodearr as if it were an indexer. The indexer API exposes candidate availability; it does not move file bytes.

The indexer surface should support:

- Capability reporting.
- Search and media-specific search parameters required by supported Arr apps.
- API key authentication using common Arr-compatible forms such as `X-Api-Key` and `apikey`.
- Categories and metadata mapping for supported media types.
- Stable release candidate GUIDs.
- Result titles, size, quality-like fields, source app/media IDs where known, and peer availability hints.
- Grab/download links that create Nodearr transfer jobs.

A Nodearr search result is a projection of a peer catalog candidate. Internally, the GUID maps back to a Nodearr release candidate, trusted peer availability, and file identity data.

## Download-client responsibility

Nodearr also behaves as a download client from the Arr ecosystem perspective.

The indexer responsibility discovers candidates. The download-client responsibility handles what happens after a candidate is grabbed.

A grab creates a Nodearr transfer job. The Arr app then needs a compatible way to observe that job until it completes or fails. The exact compatibility surface is implementation work, but the product boundary is clear: Nodearr owns transfer state, verification, failure reporting, and completed-file handoff.

The download-client-like surface should expose:

- Queued transfers.
- Active transfers.
- Progress and speed where available.
- Source peer.
- Candidate metadata.
- Verification state.
- Failure reason.
- Completed path.
- Remove/cancel support.
- Import-ready status.

The actual byte movement stays internal to Nodearr and its transfer provider abstraction.

## Profiles, matching, and content hashes

Profiles decide desirability. Hashes decide exactness.

Nodearr should not primarily ask:

**Do peers have content hash X?**

It should primarily ask:

**Does a trusted peer have a candidate that satisfies this media request and local policy?**

The matching stack should be:

1. **Media match**: Is this the same movie, episode, album, track, book, or other media item?
2. **Profile match**: Is this acceptable for my local preferences, quality profile, language profile, custom formats, or related Arr policy?
3. **Release candidate**: What specific peer file is being offered as a candidate?
4. **File identity**: What exact bytes does this candidate represent?
5. **Transfer verification**: Did I receive the exact bytes the peer advertised?

Content hashes are important, but they are not Nodearr's top-level media identity. They matter for verification, dedupe, exact-copy detection, resumable transfer, and future multi-source transfer.

For V1, Nodearr should begin with Arr metadata and profile-aware matching for search/ranking, then add whole-file content hashes for exact byte verification. Chunk hashes can come when transfer providers need resumability or multi-peer chunking.

## Node identity and trust

Each Nodearr instance is a node.

A node should have a long-lived public/private keypair. The public key is the stable node identity.

Local UI/API authentication is separate from peer trust. An API key used by a browser, Sonarr, Radarr, or Prowlarr is not a peer identity.

A trusted node relationship should be established through an invite flow:

1. John creates an invite in Nodearr.
2. Nodearr generates a short-lived code or QR code.
3. The invite contains John's public node identity, expiration data, one-time pairing material, and discovery/rendezvous metadata.
4. George enters the invite code in his Nodearr instance.
5. Both Nodearr instances exchange signed identity material.
6. Each stores the other's public key and trust record.
7. Future peer communication is authenticated using those public keys.

The short-lived invite authorizes pairing. The keypair proves ongoing identity.

## Rendezvous and direct peer connectivity

Nodearr should not require a VPN by default.

Nodearr should not require manual port forwarding as the default experience.

Nodearr may use rendezvous/discovery infrastructure to help peers find each other. That infrastructure can exchange small connection metadata such as:

- Invite IDs.
- Public node identities.
- Temporary session metadata.
- Observed addresses.
- Transport hints.
- Connection candidates.
- Expiration data.

Rendezvous infrastructure should not store catalogs permanently, own the peer graph, relay files, or carry transfer bytes.

The product promise is:

**Nodearr uses rendezvous-assisted direct peer connections. If direct peer connectivity cannot be established, transfer fails clearly unless the user explicitly configured their own alternate network path. Official Nodearr infrastructure does not relay media bytes.**

This allows Nodearr to provide a low-configuration default experience without becoming a hosted media relay.

## Iroh and UDP hole punching

Iroh is the leading peer transport direction, but it should remain behind a Nodearr transfer abstraction.

The relevant networking idea is UDP hole punching: both peers contact a discovery/rendezvous mechanism, exchange observed connection information, and then both attempt outbound communication to each other. Because both sides initiated outbound traffic, NAT/firewall state may allow a direct path to form.

Iroh is attractive because it provides lower-level networking capabilities Nodearr should not invent from scratch:

- Endpoint identity.
- Encrypted QUIC connections.
- NAT traversal.
- Discovery.
- Direct peer connection support.
- Protocol routing.
- File/blob transfer primitives.

Iroh also has relay-capable architecture, but Nodearr's official policy should reject relayed media-byte transfer through official infrastructure. The Nodearr domain should say, "connect to trusted peer George and request candidate X." The transfer provider decides how to attempt the direct connection and reports clear failure if policy prevents fallback.

## Official relay policy

Official Nodearr infrastructure must not relay media bytes.

Relay fallback is common in peer-to-peer systems, but relaying large media files would create cost, abuse, moderation, privacy, and reliability problems for the project.

The policy is:

- Discovery is acceptable.
- Rendezvous is acceptable.
- Connection hints and short-lived pairing metadata are acceptable.
- Official hosted media-byte relay is not part of V1.
- Official hosted media-byte relay should not be presented as the default fallback.
- Self-hosted relay or user-configured alternate network paths may be explored later as explicit opt-in advanced configuration.
- If direct transfer fails and no user-owned alternate path is configured, the transfer fails clearly.

## Arr-inspired product design

Nodearr should feel familiar to Arr users.

The stack and product shape are intentional:

- C#/.NET backend.
- ASP.NET Core host.
- React/TypeScript frontend.
- GPL-3.0 license.
- Arr-inspired UI shell.
- API-key machine access.
- Settings/System/Activity patterns.
- Provider-style abstractions where they reduce real integration complexity.

The UI should use Arr conventions: sidebar navigation, table-heavy pages, status pages, activity/queue pages, settings pages, logs, health warnings, and provider cards.

Nodearr's primary domain is not movies, series, albums, tracks, or books. Those remain owned by the source Arr applications.

Nodearr's primary domains are **Catalog**, **Nodes**, and **Transfers**.

Suggested routes:

### `/catalog/local`

Shows what local Arr apps say this Nodearr instance has.

### `/catalog/peers`

Shows what trusted peers say they have, compared against the local catalog.

### `/nodes/trusted`

Shows trusted Nodearr peers.

### `/nodes/invites`

Creates and redeems short-lived peer invites.

### `/activity/transfers`

Shows Nodearr transfer jobs.

### `/settings/arr-apps`

Configures Sonarr, Radarr, and later additional Arr app API connections.

### `/settings/indexer`

Configures Nodearr's Torznab/Newznab-compatible indexer surface.

### `/settings/download-client`

Configures Nodearr's Arr-facing download-client behavior.

### `/settings/transfer-providers`

Configures direct transfer providers such as local HTTP, Iroh, or future options.

### `/system/status`

Shows system status, health, and diagnostics.

## Appendix A: Application support scope

This list is intentionally iterative. Nodearr should start with a narrow, useful V1 and expand support over time.

### V1 required support

- **Sonarr**: TV series and episode catalog source.
- **Radarr**: movie catalog source.

### Planned post-V1 / iterative support

- **Lidarr**: music catalog source.
- **Readarr**: books and audiobook catalog source.
- **Whisparr / Eros**: adult media catalog source.
- **Other Arr-like applications** where the app exposes enough API metadata to support catalog import, candidate projection, and completed-download workflows.

### Downstream / compatibility role

- **Prowlarr**: downstream indexer manager. Nodearr does not catalog Prowlarr. Prowlarr may configure Nodearr as a Torznab/Newznab-compatible indexer.

### Not catalog sources

Media clients and servers such as Jellyfin or Plex are not Nodearr catalog sources. They may consume the final imported media library after normal Arr import, but they are not part of Nodearr's primary Arr integration model.

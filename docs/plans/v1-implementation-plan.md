# Nodearr V1 implementation plan

Written 2026-10-10. Read this after the handoff context: docs/overview-end-to-end-design.md, docs/adr/0001-0009, docs/research/*, docs/diagrams/*, TASKS.md.

## Confirmed repository state (2026-10-10)
- `main` is at `513d13f`. `feat/application-settings` (`d6e48d3`) is 2 commits ahead, so main can fast-forward to it.
- The docs, ADRs, research, diagrams and TASKS.md are UNTRACKED. docs/overview-end-to-end-design.md is staged as an EMPTY file, with its real content unstaged. Commit the working-copy content before anything else.
- The feature branch is Prowlarr's "Applications" sync, ported almost verbatim. It pushes a list of indexers into Sonarr as `baseUrl/{indexerId}/`. Reshape it to register ONE Nodearr Newznab indexer plus ONE Nodearr download client, with the indexer pinned to that client via Sonarr's `IndexerDefinition.DownloadClientId`. It has no Radarr support and doesn't fetch catalogs.
- The README is stale: it points at a deleted `Nodearr.Host.csproj` and `/api/v1`. The real entry point is src/NzbDrone.Console/Nodearr.Console.csproj, and the APIs are V3/V5. ADR 0004 lists logging as undecided, but the code uses NLog.
- arr_checkouts/ (Sonarr, Radarr, Prowlarr, Whisparr) is an ignored, reference-only folder.

## Decisions
1. **Arr contract:** Nodearr exposes a Newznab indexer plus a SABnzbd-compatible API (`version`, `get_config`, `fullstatus`, `addfile`, `queue`, `history`, `delete`, `retry`). A grab returns a minimal NZB carrying a Nodearr candidate ID. Sonarr's NzbValidationService (arr_checkouts/Sonarr/src/NzbDrone.Core/Download/NzbValidationService.cs) only requires an `<nzb>` root element with at least one `<file>`. The SAB `nzo_id` is the download ID. Chosen over qBittorrent emulation to avoid seeding and ratio rules. To be proven in S1.
2. **Profile matching:** Sonarr/Radarr apply their own quality profiles and custom formats to parsed result titles. Nodearr matches media by TVDB/TMDB/IMDb ID and produces accurate release titles (`sceneName`, or a title built from Arr metadata). Nodearr-side profile filtering is UI-only for V1.
3. **Hashing:** whole-file BLAKE3. The sending side hashes in the background, and on demand if a request arrives first. The hash is fixed when the transfer starts.
4. **Rust/.NET boundary:** a Rust `nodearr-transport` sidecar that .NET starts and supervises. They talk over loopback with a shared token.
5. **Identity:** the Iroh Ed25519 key is the node identity. It's stored as a file in the data directory, not in the settings database.
6. **Transport:** an in-memory fake for tests and Iroh for real traffic. No throwaway LAN HTTP transport (this replaces the M4 "local HTTP" step).
7. **Sharing default (owner decision):** a newly trusted peer sees NOTHING until something is shared with them. The pairing flow offers a one-click "share everything except Hidden".
8. **Relay (owner decision):** by default, n0's public Iroh relays are used for coordination only, and media bytes are only sent over direct paths. Users can configure their own relay URL. Official infrastructure never relays media.
9. **Second node (owner decision):** a friend's PC on another network, used for the cross-NAT spike (S2) and final acceptance (S8).
10. **Inherited Sonarr TV features:** list them in S0, and remove them only where they get in the way.

## Slices
- **S0: save and baseline.** Commit the docs and overview on a local branch based on feat/application-settings (no push). Fix the README and ADR 0004, and add a note on reused Sonarr code (GPL-3.0). List the inherited Sonarr features. Done when `dotnet build src/Nodearr.sln`, `yarn build`, and running Nodearr.Console all work, and V3/V5 status responds.
- **S1: Spike A, the Arr contract.** Stub Newznab (`caps`/`tvsearch`/`movie`/`get`) and SAB APIs backed by a local file copy. Pinned Sonarr, Radarr and Prowlarr containers share a `/downloads` volume. Scripted proof of search -> grab -> NZB accepted -> queue -> history Completed -> ACTUAL IMPORT, both directly and through Prowlarr, plus a failure path that Arr blocklists. Record as ADR 0010 and update the search/grab diagram with the real messages.
- **S2: Spike B, Iroh direct-only transfer.** A standalone Rust binary on a pinned Iroh 1.x. Compare strict mode (relay disabled) with relay-for-coordination where every chunk is held back unless the path is direct. Count relayed bytes with a self-hosted `iroh-relay`. Decide between `iroh-blobs` with an authorization hook and a custom ALPN stream. Pass when same-LAN and cross-NAT transfers succeed and a relay-only case fails clearly with ZERO media bytes relayed. Record as ADRs.
- **S3: catalog import (M1, M2).** Arr Apps settings for Sonarr and Radarr, with auto-registration of the indexer and download client. Import series, episodes, episode files, movies, movie files and profiles into normalized tables, including multi-episode files. Path mappings, scheduled and manual refresh, marking missing items and purging them. `/catalog/local` page.
- **S4: identity, invites, trust and blocking (M3).** One-time expiring invites and a signed handshake. States: pending, trusted, blocked, revoked. A block is rejected when the connection is accepted, purges the cached catalog and cancels jobs.
- **S5: sharing lists and filtered catalog exchange (M4, M5).** A Hidden list plus share lists aimed at specific peers. Filtering happens on the server before anything is sent. Versioned per-peer snapshots that drop revoked items. `/catalog/peers` shows what's missing, what's an upgrade and what you already have.
- **S6: matching and real indexer (M6, M10).** ID-based search, TV/movie categories, signed opaque GUIDs, RSS off by default.
- **S7: transfers and download client (M7-M9, M11).** A job state machine with typed failures (NoDirectRoute, NotAuthorized, Revoked, VerificationFailed, DiskError, Cancelled). The sender re-checks authorization on every request. Files go to an incomplete folder, are verified, then moved into place in one step. Resuming on restart, idempotent `addfile`, cancel via `delete`, and a live `/activity/transfers` page over SignalR.
- **S8: Docker and two-machine acceptance.** A multi-stage image (.NET, yarn, cargo), compose files, `/config` (database and key) and shared `/downloads` volumes, read-only library mounts, PUID/PGID, and UDP/host networking. Full flow on two PCs, plus hidden-content denial, blocking mid-transfer, injected corruption, restart mid-transfer, and a no-direct-route failure with nothing relayed.

Order: S0, then S1 and S2 together as go/no-go gates, then S3 through S8 in sequence. If either spike disproves its approach, stop and revisit with the owner.

Completion means a real Sonarr/Radarr import of a verified, permitted peer transfer between two deployed instances.

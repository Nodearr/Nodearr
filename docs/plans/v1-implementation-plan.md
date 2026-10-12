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

## Status (updated 2026-10-10)

- **S0: done.** Branch `v1/s0-baseline`.
- **S1: done, go.** Nine of nine scenarios pass against Sonarr 4.0.20, Radarr 6.4.4 and Prowlarr 2.6.5. ADR 0010. Branch `v1/s1-s2-spikes`.
- **S2: passes on one machine, gate still open.** Seven of seven scenarios pass, including against n0's public relays. The cross-NAT run on two machines has not been done. ADRs 0011 and 0012 are Proposed until it has. The owner chose to proceed with S3 on the assumption that it will pass.
- **S3: done.** Fourteen of fourteen acceptance checks pass (`acceptance/Run-S3CatalogImport.ps1`). Branch `v1/s3-catalog-import`.
- **S4: done.** Eleven of eleven acceptance checks pass between two real instances on one machine (`acceptance/Run-S4Pairing.ps1`). ADR 0013. Branch `v1/s4-identity-trust`.
- **S5: done.** Ten of ten acceptance checks pass between two real instances with real Arr catalogs (`acceptance/Run-S5Sharing.ps1`). ADR 0014. Branch `v1/s5-sharing-catalog-exchange`.
- **S6: done.** Fourteen of fourteen acceptance checks pass: a second Sonarr and Radarr search Nodearr and find, parse and approve what a trusted node shares (`acceptance/Run-S6Indexer.ps1`). ADR 0015. Branch `v1/s6-matching-indexer`.
- **S7: done.** A grab in Sonarr or Radarr ends in an import of a file fetched from a trusted node and verified (`acceptance/Run-S7Transfers.ps1`). ADR 0016. Branch `v1/s7-transfers`.
- **S8: done on one machine, two-machine run still open.** One Docker image holds the UI, the application and the sidecar. Two complete deployments, each with its own Sonarr and Radarr, do the full flow and fail correctly in the five cases the plan lists (`acceptance/Run-S8Docker.ps1`). ADR 0017. Branch `v1/s8-docker`. The run between two PCs on different networks needs the owner and a second machine: `docs/guides/two-machine-test.md`.

Owner decisions made after the plan was written:

- The default port is 6633.
- Port mapping (UPnP, NAT-PMP, PCP) is on by default, with a setting to turn it off.
- When the catalog is empty, the indexer answers ID-less queries with a stand-in item so the Arr apps accept it (ADR 0010, amendments).

What S3 left for later slices:

- The endpoints Nodearr registers in the Arr apps are still the S1 stubs. The real indexer is S6 and the real download client is S7. The download folder the stub reports is fixed at `/downloads/complete`.
- Quality profiles are imported but nothing shows or uses them yet.
- Deleting an application in Nodearr does not remove Nodearr's indexer and download client from the Arr app.
- Sonarr import makes two requests per series. It has not been tried on a large library.

What S4 left for later slices:

- Blocking and revoking raise an event, but there is no peer catalog to purge (S5) or transfer to cancel (S7) yet.
- Relay mode, relay URL and port mapping are environment variables, with no settings page (S8).
- The transport sidecar is built separately with cargo and is not yet part of a package or image (S8).
- The node's display name is the machine name.
- Pairing has only been run on one machine with relays off.

What S5 left for later slices:

- Sharing is by whole series or movie. There is no per-season or per-file control.
- Peer catalog rows are not yet indexer results (S6), and a transfer request is not yet checked against the sharing rule (S7).
- An offer is rebuilt from the whole local catalog on each request. It has not been tried on a large library.

What S6 left for later slices:

- A grab reaches Nodearr's download client, which is still the S1 stub and refuses it with a plain reason. Transfers are S7.
- ~~Daily shows are not found, because peers are not told air dates.~~ Done afterwards, see below. Series with scene numbering can be misnumbered when a file has no release name.
- Nodearr-side profile filtering (decision 2, "UI-only") was not built. The peer catalog page shows the title the Arr apps will see, and the Arr apps do the judging.
- The S1 spike harness no longer runs on later branches, because the indexer half of the stub is gone. The acceptance scripts now fill their test libraries by having the Arr apps import files directly.

What S7 left for later slices:

- Everything has run with both nodes on one machine. Two machines on different networks, and a transfer with no direct route, are S8.
- Files are hashed when first asked for, not in the background.
- ~~Uploads are not shown on the Transfers page. The upload limit and the download folder are options with no settings page.~~ Both done afterwards, see below.
- Unsharing a title does not stop a transfer of it that is already under way. Ending trust does.
- The sidecar is still built separately with cargo.

What S8 left:

- **The two-machine run.** It is the plan's last acceptance item and the open gate on ADR 0011 and ADR 0012. Until it is done, "files only travel over a direct connection" is proven between containers and on one machine, and not between two homes.
- No image is published, and only linux/amd64 has been built.
- ~~The transport options are environment variables, with no settings page.~~ Done afterwards, see below.
- ~~The inherited update check and error reporting still point at nodearr.com hosts that do not exist yet.~~ Removed afterwards, see below.

## After the plan (2026-10-11)

Three things were added before the two-machine run, to make it worth running. ADR 0018. Branch `v1/s9-diagnostics`.

- "Check connection" says whether two nodes reach each other directly or only through a relay, and the Nodes page says what is known about the local network.
- System > Status warns when Nodearr cannot read the library, and when Sonarr or Radarr cannot see Nodearr's download folder.
- The inherited update check, time check, notifications, error reporting and analytics are gone. A node contacts only what it is pointed at.

Then a settings page. ADR 0019. Branch `v1/s10-node-settings`.

- The Transfers page shows what this node is sending as well as what it is fetching, live, and how each upload ended. Branch `v1/s11-uploads`.
- A trusted node reached only through a relay is a warning on System > Status, and it says to check each machine's firewall before forwarding a port. The two-machine guide has a firewall section. Found because Windows blocked the transport on the development machine and every same-machine test still passed.
- Connections are checked without anyone asking: after pairing, every 15 minutes, when a transfer finds no route, and when a file arrives over a route the last check did not call direct. A transfer that finds no direct route says what the last check found and which side to look at, in Nodearr and in the Arr app's history. Deleting an application takes Nodearr back out of the Arr app. Branch `v1/s13-connection-upkeep`.
- Daily shows are found. The day each episode aired travels in the offer, a query by day is matched by day, and the title is written the way Sonarr asked. Checked with a real Sonarr set to daily. Branch `v1/s14-links-and-daily-shows`.
- Links to pages that do not exist are gone from every page the app leads to: the "More Info" block on System > Status, the donate button in the header, three wiki links, and the wiki addresses the API handed out. Inherited pages that no route leads to still hold theirs.
- Settings > General is a real page: where the page is served, signing in, the API key, and logging. A reset API key is given to Sonarr and Radarr at once. In a container the port and address are fixed by the image. A restart from the page works, on the host and in a container, after fixing a crash it uncovered. The inherited series search box is gone from the header. Branches `v1/s15-header-search` and `v1/s16-general-settings`.
- Settings > Node holds the node's name, relay, UDP port, port mapping, wait for a direct route, download folder and upload limit. Bad values are refused field by field. A setting given in the environment is shown and cannot be changed. A change that needs it restarts the transport and nothing else, and the node keeps its identity and its trust (`acceptance/Run-NodeSettings.ps1`).

## Where V1 stands

The plan's definition of done is "a real Sonarr/Radarr import of a verified, permitted peer transfer between two deployed instances". That has been met between two deployed instances on one machine, many times over, with the failure cases checked. It has not been met between two machines.

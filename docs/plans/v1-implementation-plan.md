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

## Status of the slices (updated 2026-10-14)

- **S0: done.** Branch `v1/s0-baseline`.
- **S1: done, go.** Nine of nine scenarios pass against Sonarr 4.0.20, Radarr 6.4.4 and Prowlarr 2.6.5. ADR 0010. Branch `v1/s1-s2-spikes`.
- **S2: passes on one machine, and the gate has since closed.** Seven of seven scenarios pass, including against n0's public relays. The owner chose to proceed with S3 on the assumption that the run across two networks would pass. It was made on 2026-10-12 and 2026-10-13, and ADRs 0011 and 0012 were accepted on it.
- **S3: done.** Fourteen of fourteen acceptance checks pass (`acceptance/Run-S3CatalogImport.ps1`). Branch `v1/s3-catalog-import`.
- **S4: done.** Eleven of eleven acceptance checks pass between two real instances on one machine (`acceptance/Run-S4Pairing.ps1`). ADR 0013. Branch `v1/s4-identity-trust`.
- **S5: done.** Ten of ten acceptance checks pass between two real instances with real Arr catalogs (`acceptance/Run-S5Sharing.ps1`). ADR 0014. Branch `v1/s5-sharing-catalog-exchange`.
- **S6: done.** Fourteen of fourteen acceptance checks pass: a second Sonarr and Radarr search Nodearr and find, parse and approve what a trusted node shares (`acceptance/Run-S6Indexer.ps1`). ADR 0015. Branch `v1/s6-matching-indexer`.
- **S7: done.** A grab in Sonarr or Radarr ends in an import of a file fetched from a trusted node and verified (`acceptance/Run-S7Transfers.ps1`). ADR 0016. Branch `v1/s7-transfers`.
- **S8: done.** One Docker image holds the UI, the application and the sidecar. Two complete deployments, each with its own Sonarr and Radarr, do the full flow and fail correctly in the five cases the plan lists (`acceptance/Run-S8Docker.ps1`). ADR 0017. Branch `v1/s8-docker`. The runs between two machines on different networks followed: home to cloud on 2026-10-12 and 2026-10-13, and home to home on 2026-10-14 (ADR 0011, "Runs between two machines").

Owner decisions made after the plan was written:

- The default port is 6633.
- Port mapping (UPnP, NAT-PMP, PCP) is on by default, with a setting to turn it off.
- When the catalog is empty, the indexer answers ID-less queries with a stand-in item so the Arr apps accept it (ADR 0010, amendments).
- 2026-10-14, the owner's overall direction: work with the Arr ecosystem, not against it. Nodearr's concepts, units and schedules follow Sonarr's and Radarr's.
- 2026-10-14: sharing is per file, which for TV is per episode, the way Sonarr monitors a whole series, a season or single episodes. Movies are already per file.
- 2026-10-14: a changed catalog is sent as only what changed, not whole. Designed on the Arr apps' conventions:
  - A Catalog Sync Interval, polled like Sonarr's RSS Sync: in minutes, default 15, 10 to 120, or 0 to turn it off.
  - Nodearr hears of new, upgraded and deleted files from Sonarr and Radarr through a webhook connection, which it adds itself as it adds its indexer and download client. It is never set up by hand.
  - The full import from each Arr app runs every 12 hours, as Sonarr's Refresh Series does.
  - Removals are still pushed to peers at once, so unsharing reaches them in seconds.
  - The same work puts each file's BLAKE3 hash in the offer, so a node can tell when two peers hold the same bytes: identical copies show as one result in the Arr apps, and a transfer that fails from one peer can carry on from another that holds the same file. Fetching parts from several peers at once is later, and only if identical copies turn out to be common.
- Release builds will eventually match Sonarr's and Radarr's platforms. Not a focus yet. Done afterwards, see below.

Each slice listed what it left for later. Those lists are gone: everything on them was done by a later slice or in the work after the plan (below), apart from what is under "What is still open" at the end. They are in this file's history.

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
- A sign-in page. A fresh install made its owner set up signing in and then sent the browser to a page that did not exist. Found on the first real deployment, on a rented machine. Branch `v1/s18-sign-in-page`.
- Unsharing a title stops an upload of it that is already under way. So does the file leaving the Arr app's library. The receiving node fails the transfer with `NotAuthorized` and does not try again (ADR 0016, amendments). Branch `v1/s23-unshare-stops-upload`.
- Shared files are hashed in the background, and the hashes are kept, so a request does not wait while a large file is read. The Sharing page shows how far it has got (ADR 0016, amendments). Branch `v1/s24-background-hashing`.
- A large library was measured: 66,000 files in a real database (ADR 0014, amendments). Building and exchanging the offer, storing a peer's catalog and the indexer were all well under a second, apart from storing a whole new catalog at 6 s. A file request rebuilt the whole offer to check one file, which took 0.65 s. It now checks the file directly, in 7 ms. A hashing pass found each shared file with its own query, which took 5 s. It now takes under 1 s. What remains: a changed offer is sent whole, 36 MB at this size, and Sonarr import, at two requests per series, was not measured against a real Sonarr that large. Branch `v1/s25-large-library`.
- A file that fails verification is tried again from nothing, and the sending node forgets the hash it sent, so a file that changed without its size or modified time changing costs one attempt and not the release (ADR 0016, amendments). Branch `v1/s26-verification-retry`.
- Series the scene numbers differently from TVDB are titled and matched by the scene's numbers, which is how Sonarr reads them. Before, a built title named the wrong episode: checked against a real Sonarr with American Dad! (ADR 0015, amendments). Branch `v1/s27-scene-numbers`.
- Sharing is by series, season or episode, as Sonarr monitors, on share lists and the Hidden list alike. A file of several episodes is shared only when all of them are (ADR 0014, amendments). Branch `v1/s28-per-episode-sharing`.
- A node that holds a peer's catalog is sent only what changed. Each offered file carries a fingerprint and, where worked out, its BLAKE3 hash. On 66,000 files one changed episode costs 809 bytes instead of 38 MB (ADR 0014, amendments). Branch `v1/s29-catalog-changes`.
- A node asks its peers what changed every Catalog Sync Interval, 15 minutes by default, set like Sonarr's RSS Sync Interval. Peers are told at once only of removals. The whole library is read from the Arr apps every 12 hours (ADR 0014, amendments). Branch `v1/s30-catalog-sync-interval`.
- Sonarr and Radarr tell Nodearr when files come and go, through a webhook connection Nodearr adds to them itself. Nodearr reads that one series or movie again, so the catalog follows the library within seconds (ADR 0006 and ADR 0010, amendments). Branch `v1/s31-arr-webhook`.
- The same file from several trusted nodes is one result in the Arr apps, and a transfer that fails from one of them carries on from another, from where it was (ADR 0015 and ADR 0016, amendments). Branch `v1/s32-identical-copies`. That completes the owner's decisions of 2026-10-14 on catalog exchange.
- Release packages for Sonarr's and Radarr's platforms, less FreeBSD: Linux glibc and musl on x64, arm64 and 32-bit ARM, macOS on Intel and Apple silicon, Windows 64- and 32-bit. Each is self-contained with the web UI and its sidecar. The image builds for amd64 and arm64. A pushed `v*` tag drafts a GitHub release and pushes the image, and every push runs CI: lint, build, unit tests on Linux and Windows, the sidecar and the image (`docs/guides/releases.md`). Branch `v1/s33-releases`.
- Settings > Node holds the node's name, relay, UDP port, port mapping, wait for a direct route, download folder and upload limit. Bad values are refused field by field. A setting given in the environment is shown and cannot be changed. A change that needs it restarts the transport and nothing else, and the node keeps its identity and its trust (`acceptance/Run-NodeSettings.ps1`).
- The two-machine test runs on two Windows machines with nothing set up, no Docker and no Python, from the release packages: `tools/two-machine-test.ps1` and `docs/guides/two-machine-test-windows.md`. It made the home-to-home runs. Branch `v1/s36-windows-two-machine-test`.
- A first run that sets itself up. Home, the page the app opens on, was a placeholder. It now welcomes a new node and takes it through three steps, each ticked off from what the node reports: connect Sonarr or Radarr, choose the name friends see, and connect with a friend by creating an invite or pasting one. The page moves on by itself when the friend uses the invite. Once all three are done it is an overview: anything that needs attention, what friends have that this node is missing or has worse, the library, the friends and how each is reached, and what the node knows about its network. Catalog and Nodes in the sidebar go to real pages, and the app form no longer shows Prowlarr's tags or describes its sync level in Prowlarr's terms. Branch `v1/s38-first-run`.
- System has its pages back: Tasks, Backup, Events and Log Files, as Sonarr has them. A backup now holds the node's key as well as its settings and database, so a restored node is the node its friends paired with and nobody pairs again. The key is put in place when the transport next starts, never under a running one (ADR 0013, amendments). The Sonarr recycle bin task and the updater's log files are gone. Branch `v1/s39-system-pages`.
- What a friend has is marked an upgrade only when the local Arr app's quality profile would take it: allowed, ranked higher, upgrades on, and the local file below the cutoff (ADR 0014, amendments). Before, any higher resolution or better source counted. Branch `v1/s40-profile-upgrades`.
- Prowlarr can be added in Settings > Apps. Nodearr adds itself to it as an indexer, to be searched there, with a sync profile that switches off the copies Prowlarr makes in Sonarr and Radarr, which would not be pinned to Nodearr's download client. Prowlarr holds no library and Nodearr reads nothing from it (ADR 0007, amendments). Run against a real Prowlarr managing a real Radarr. Branch `v1/s41-prowlarr`.
- A Windows installer, as Sonarr has: Nodearr in Program Files, starting from the tray at sign-in, as a Windows service, or not at all. Installing adds a firewall rule for the transport, which a service could not otherwise get, and uninstalling removes it. The tray app's icon has every size Windows asks for. Built by the release workflow with Inno Setup (`docs/guides/releases.md`). Branch `v1/s42-windows-service-tray`.
- Whisparr v3 (Eros) can be added, as Radarr can. Its films are found by ID and its scenes by Whisparr's text search, studio, date, title and code (ADR 0015, amendments). Adult content is found only by an adult search, never by Radarr's. Run end to end between two Whisparr 3.6.5 instances. Whisparr v2 and Lidarr are next; Readarr is not, as it was archived in June 2025. Branch `v1/s44-whisparr-v3`.
- Whisparr v2 can be added as well, its scenes kept as v3 keeps them, so a v2 and a v3 find each other's scenes. Run end to end between two v2 instances and between a v2 and a v3 (ADR 0015, amendments). Branch `v1/s45-whisparr-v2`.
- Lidarr can be added, and its library of artists, albums and track files is read into the catalog and kept current by its webhook. Artists can be shared, and whole albums are offered with their tracks and compared with the local Lidarr by release group. An album is moved as a folder of tracks, appearing for Lidarr only once every track is verified, and Lidarr finds albums by artist and album (ADR 0020). Run end to end between two Lidarr 3.1 instances. Branch `v1/s46-lidarr`.
- Pair attempts are limited. A node that keeps failing to pair is turned away for 15 minutes, a flood of failures is counted rather than logged one by one, and System > Status says when it happens. Pairing is never closed to everyone (ADR 0013, amendments). Branch `v1/s47-pair-rate-limit`.
- The docs describe Nodearr as it is. A user guide in `docs/user-guide/`, a README and overview rewritten from the code, every diagram and decision record brought up to date, a changelog, a contributing guide and a security policy. Every package and the image carry third-party notices, generated by `build/Build-Notices.ps1`. Branch `v1/s48-docs`.
- A large Sonarr was measured: 2,000 series, 174,044 episodes and 101,223 files read in 36 s, 32 s with nothing changed, one series from a webhook in 1 s, and Nodearr settled at 382 MB with the catalog in place. Nothing needed changing (ADR 0006, amendments). Branch `v1/s49-large-sonarr`.
- An album's tracks arrive named as Lidarr names its own, `Artist - Album - 01 - Title`, from the titles a friend's Lidarr knows, and a Lidarr that does not rename keeps them so (ADR 0020, amendments). Branch `v1/s50-lidarr-track-titles`.

## Where V1 stands

The plan's definition of done is "a real Sonarr/Radarr import of a verified, permitted peer transfer between two deployed instances". That has been met between two deployed instances on one machine, many times over, with the failure cases checked. On 2026-10-12 it was met between two machines on different networks: a PC at home and a rented cloud machine, with no port forwarded on the home router (ADR 0011, "Runs between two machines"). The cloud machine had to allow UDP in for the connection to be direct, because the home side ran in Docker Desktop.

On 2026-10-13 the same two machines connected directly and moved a file with nothing opened on either side: no port forwarded at home, and no UDP allowed in at the cloud machine. The home side ran Nodearr straight on Windows that time. ADR 0011 and ADR 0012 were accepted on it. That is V1's definition of done, met.

On 2026-10-14 two home networks connected directly and moved a file: two Windows machines behind ordinary routers, with no port forwarded, and in the second run with Nodearr's port mapping off on both sides and on fresh UDP ports (ADR 0011, "two home networks"). That was the last connection case V1 had not tried. A node in Docker Desktop on Windows or macOS remains hard to reach directly without a forwarded port.

## What is still open

V1 is done. What follows is open, and none of it needs a second machine except where it says so.

Not yet run:

- **Sonarr between two machines.** Every run between two machines used Radarr and one film. What Sonarr adds, sharing by episode, scene numbers and daily shows, is Nodearr's own logic and has been checked against a real Sonarr on one machine.
- **Living with it** (needs two machines). Every run so far was one pairing and one file. Nothing has run for days: the Catalog Sync Interval, the Arr webhooks, the 12-hour import, restarts on either side, a node going away and coming back.
- **Large files between networks.** The largest transfer between two networks was 89 MB. Multi-gigabyte files and resuming after a lost connection have been checked on one machine only.
- **A network that cannot connect directly** (needs two machines), such as a phone's mobile data. It should say "Relay only" and fail as `NoDirectRoute`. That has been seen only against a cloud machine.
- **Release packages that have not been started:** macOS on Intel and Apple silicon (needs a Mac), linux-musl-arm64, and 32-bit ARM on a real board. The image has not been pulled back from GHCR.
- **A blocking firewall rule that did not block.** In the home-to-home runs the receiving machine had a rule blocking `nodearr-transport`, and both runs connected. Most likely because that side opened the connections. Not checked.
- **Relay bytes when a direct path is lost part way.** The sidecar resets the stream, and a connection has survived a firewall change, but nobody has counted what the relay carried in that case (ADR 0011).
- **The systemd unit** in the user guide, modelled on Sonarr's, has not been run.

Not built:

- Custom formats in Nodearr's marks. Upgrades follow the quality profile's qualities, and the Arr apps judge custom formats themselves when they search (decision 2).
- A FreeBSD package and a macOS app bundle (`docs/guides/releases.md`). No updater, by design (ADR 0018).
- Inherited pages that no route leads to still hold links to pages that do not exist.
- The S1 spike harness no longer runs on later branches. The acceptance scripts replaced it.
- The web UI still holds Sonarr's error reporting code, pointed at `sentry.nodearr.com`. It never runs, because analytics is always off (ADR 0018), but it should go.

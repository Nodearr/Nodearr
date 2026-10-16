# ADR 0006: Arr Apps as Catalog Source of Truth

## Status

Accepted.

## Context

Sonarr, Radarr, and related applications already understand media identity, library membership, file paths, quality, profiles, custom formats, and import workflows. Rebuilding that logic from folder scans would duplicate mature application behavior and produce worse identity decisions.

Nodearr's catalog needs to know what a local user has and what trusted peers have, but Nodearr should not become the primary media library manager.

## Decision

Nodearr will treat configured Arr applications as the catalog source of truth.

For V1, Nodearr must import catalog data from:

- Sonarr for TV series and episodes.
- Radarr for movies.

Nodearr will normalize imported Arr data into Nodearr catalog entries and peer availability records.

Nodearr should not primarily scan folders and infer media identity itself. File hashing may read files later for verification and exact byte identity, but hashing is not the source of media identity.

Media clients such as Jellyfin or Plex are not catalog sources. They may consume the final library after Arr import, but they are outside Nodearr's primary integration model.

## Consequences

### Positive

- Nodearr benefits from existing Arr media identity and profile logic.
- V1 scope stays narrow and useful.
- Completed transfers can flow back through normal Arr import workflows.
- Future Arr app support can be added source by source as metadata coverage is understood.

### Negative / trade-offs

- Nodearr depends on configured Arr app availability and API behavior.
- Non-Arr libraries are outside the core model unless future integrations are explicitly designed.
- Normalization must account for differences between Sonarr and Radarr.
- Peer catalog comparisons need to handle missing or app-specific metadata.

## Alternatives considered

### Primary folder scanner

Rejected. It would duplicate Arr functionality and make Nodearr responsible for media identity inference.

### Prowlarr as catalog source

Rejected. Prowlarr manages indexers; it does not own the user's media library.

### Media server as catalog source

Rejected for core scope. Media servers consume the imported library but do not define the Arr automation/import workflow.

## Follow-up work

- ~~Define Sonarr import fields and sync behavior.~~ Done: S3, and the [catalog import diagram](../diagrams/catalog-import-sonarr-radarr.md).
- ~~Define Radarr import fields and sync behavior.~~ Done: S3.
- ~~Define normalized catalog schema.~~ Done: S3.
- ~~Define stale/deleted item handling.~~ Done: an item the app stops reporting is marked missing and removed after 7 days (S3).
- ~~Define future source contracts for Lidarr, Readarr, Whisparr / Eros, and other Arr-like applications.~~ Done for Whisparr v2 and v3 (ADR 0015, amendments) and Lidarr (ADR 0020). Readarr was archived in June 2025 and is not supported.

## Amendments

- 2026-10-14: **the catalog follows the Arr apps within seconds.** Until now Nodearr read each Arr app's whole library on a schedule, every 6 hours, so a file imported or deleted in Sonarr was not seen by Nodearr for hours. Now:
  - Sonarr and Radarr call Nodearr's webhook (ADR 0010, amendments) when a file is imported, upgraded, renamed or deleted, and when a series or movie is added or deleted.
  - Nodearr reads only that series or movie again from the Arr app and merges it into the catalog, as the full read does. A series or movie the Arr app no longer has is marked missing, with everything in it. Nothing else of the library is touched. The webhook's body is used only for the ID of the series or movie, so the catalog always holds what the Arr app says, not what an event claimed.
  - Several events about one title close together, such as a season imported file by file, become one read, because Sonarr's command queue merges identical commands that are waiting.
  - The whole library is still read on a schedule, now every 12 hours as Sonarr refreshes its series, to catch anything a webhook missed, for example while Nodearr was down.
  - Checked by unit tests and by `acceptance/Run-S5Sharing.ps1` against a real Sonarr: a file deleted in Sonarr left the other node in 1.1 seconds with nobody asking, and a file imported again was in the catalog 1.0 second after the import was started.
- 2026-10-16: **a large Sonarr, measured.** Sonarr 4.0.20.3014 on Windows, its database filled with 2,000 series, 174,044 episodes and 101,223 episode files (multi-episode files, media info and release names included, no metadata fetched), read by a fresh Nodearr on the same machine:

  | What | Time |
  | --- | --- |
  | Adding Sonarr: the test, and registering the download client, indexer and webhook | 0.1 s |
  | The first full read, two requests per series | 36 s |
  | A full read with nothing changed, as every 12 hours | 32 s |
  | One series again from the webhook, the largest (166 episodes) | 1.0 s |
  | Catalog > Local, first page | 0.25 s |
  | Catalog > Sharing, every title | 0.56 s |

  Most of a read is Sonarr answering 4,000 requests one after another: its own `/series` took 1.5 s, and the largest series' episodes and files 0.02 s and 0.13 s. Nodearr's working set rose from 159 MB to about 600 MB at its peak, during the reads and the Sharing page, and settled at 382 MB once .NET gave memory back, about two minutes later, with the whole catalog in place. Sonarr peaked at 240 MB. Nodearr's database came to 53 MB.

  This is a library larger than most. Nothing needed changing. A Sonarr on slower disks, or across a network, makes each request slower, and so the read longer, but it runs in the background and the webhook keeps the catalog current in between.


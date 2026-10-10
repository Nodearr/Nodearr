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

- Define Sonarr import fields and sync behavior.
- Define Radarr import fields and sync behavior.
- Define normalized catalog schema.
- Define stale/deleted item handling.
- Define future source contracts for Lidarr, Readarr, Whisparr / Eros, and other Arr-like applications.


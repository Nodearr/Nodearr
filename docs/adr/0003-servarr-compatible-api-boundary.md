# ADR 0003: Torznab/Newznab Indexer Boundary

## Status

Accepted.

## Context

Nodearr should integrate with Arr automation. Arr applications and indexer managers already understand Torznab/Newznab-style indexer APIs for discovery and grab flows.

Nodearr's indexer role is to expose peer-available candidates. The indexer boundary must not leak peer internals or move media bytes.

## Decision

Nodearr's first search compatibility boundary will be a Torznab/Newznab-compatible indexer API.

The boundary should provide:

- Capability reporting.
- Search endpoints compatible with [Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/) V1 needs.
- API-key authentication using `X-Api-Key` and `apikey`.
- Categories and media identifiers where available.
- Release candidate results with stable GUIDs, titles, sizes, quality-like fields, source metadata, peer availability hints, publish dates where meaningful, and grab links.
- Grab/download links that create local Nodearr transfer jobs instead of returning arbitrary peer internals.

[Prowlarr](https://prowlarr.com/) may consume this API as an indexer manager, but Prowlarr is downstream of Nodearr and is not a catalog source.

Nodearr will maintain a separate native API for its own UI and domain operations.

## Consequences

### Positive

- Existing Arr apps can discover Nodearr results using familiar indexer configuration.
- Prowlarr compatibility is plausible because the exposed shape resembles known indexers.
- Nodearr can keep peer trust, catalog sync, transfer provider details, and content hashes internal.
- The compatibility surface remains narrow enough to test against real Arr behavior.

### Negative / trade-offs

- Nodearr must translate peer catalog candidates into release-like search results.
- Sonarr and Radarr have different query and category expectations.
- Some metadata may be incomplete until matching and normalization mature.
- Grab links have to satisfy indexer semantics while creating Nodearr-owned transfer jobs.

## Alternatives considered

### Native API only

Rejected. It would not integrate directly with existing Arr automation.

### Download-client API first

Rejected as the first discovery boundary. It would still require another indexer to discover peer candidates.

### Full Prowlarr-compatible indexer management API

Deferred. Nodearr should first be searchable and grabbable as an indexer, not reproduce indexer-management APIs.

## Follow-up work

- ~~Define exact Torznab/Newznab parameters and response formats required by Sonarr and Radarr.~~ Done: ADR 0010 and ADR 0015. Only Newznab is served.
- ~~Define category mapping for TV and movies.~~ Done: ADR 0015.
- ~~Define future category expansion for [Lidarr](https://lidarr.audio/), [Readarr](https://github.com/Readarr/Readarr), [Whisparr](https://whisparr.com/) / Eros, and other Arr-like applications.~~ Done for Whisparr (ADR 0015, amendments) and Lidarr (ADR 0020). Readarr was archived in June 2025 and is not supported.
- ~~Define release candidate GUID format.~~ Done: ADR 0015.
- ~~Define grab endpoint behavior and error responses.~~ Done: ADR 0010 and ADR 0015.
- ~~Build compatibility tests against local Sonarr/Radarr/Prowlarr expectations.~~ Done: the S1 spike and the acceptance scripts in `acceptance/`.

## Amendments

- 2026-10-19: **a grab link returns a ticket.** The original decision described grab links as creating a transfer. As built, `t=get` only returns the NZB ticket. A job is created when the Arr app uploads it to `/sabnzbd/api` with `mode=addfile` ([ADR 0010](0010-arr-contract-newznab-indexer-and-sabnzbd-download-client.md)).


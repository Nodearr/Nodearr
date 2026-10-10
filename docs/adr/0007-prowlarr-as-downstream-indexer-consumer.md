# ADR 0007: Prowlarr as Downstream Indexer Consumer

## Status

Accepted.

## Context

Prowlarr is an indexer manager in the Arr ecosystem. It can help users configure indexers once and share them with Arr applications.

Prowlarr does not own the user's media library, file paths, quality profiles, or completed-download import workflow. Treating it as a catalog source would put Nodearr on the wrong side of the architecture.

## Decision

Prowlarr is downstream of Nodearr.

Nodearr may expose a Torznab/Newznab-compatible indexer API that Prowlarr can consume. Prowlarr may then make Nodearr searchable by Sonarr, Radarr, and future supported Arr applications.

Nodearr must not import catalog data from Prowlarr and must not treat Prowlarr as a source of media library truth.

## Consequences

### Positive

- Nodearr fits existing indexer-manager workflows.
- Users who already centralize indexers in Prowlarr can add Nodearr there.
- Catalog source responsibility remains with Sonarr, Radarr, and future media-owning Arr apps.

### Negative / trade-offs

- Nodearr still needs direct configuration options for users who do not use Prowlarr.
- Prowlarr compatibility may expose edge cases in Torznab/Newznab responses.
- Search/grab testing must cover both direct Arr and Prowlarr-mediated flows.

## Alternatives considered

### Prowlarr as catalog source

Rejected. Prowlarr does not track the user's owned library in the way Nodearr needs.

### Require Prowlarr

Rejected. Nodearr should work when configured directly in supported Arr apps.

## Follow-up work

- Test Nodearr indexer configuration through Prowlarr.
- Document direct Arr setup and Prowlarr-mediated setup separately.
- Keep catalog import settings separate from indexer exposure settings.


# ADR 0001: Nodearr Product Shape

## Status

Accepted.

## Context

The Servarr ecosystem centers on self-hosted applications with local web UIs, API-key protected machine APIs, indexer integrations, download-client integrations, queues, history, settings, logs, and health checks.

Nodearr is not another media library manager and is not a public indexer. It is a trusted private peer-to-peer media availability and transfer service intended to fit into existing Arr workflows.

The central product boundary is:

**Arr apps define what the media is. Nodearr defines who has an acceptable candidate and how to retrieve it.**

V1 must support Sonarr and Radarr as catalog sources. Additional Arr-like applications are future iterative scope.

## Decision

Nodearr will be shaped as a self-hosted service with:

1. A local web UI for administration and visibility.
2. A Nodearr-owned native API for catalog, nodes, invites, transfers, providers, health, logs, and settings.
3. Sonarr and Radarr catalog importers for V1.
4. A Torznab/Newznab-compatible indexer boundary for search and grab flows.
5. An Arr-compatible download-client boundary for transfer job state and completed-file handoff.
6. A transfer provider abstraction for direct peer-to-peer byte movement.
7. A trust-first peer model separate from local UI/API authentication.

Nodearr should adopt familiar Arr product patterns and workflows. Direct implementation-code reuse from existing Arr projects is not assumed; any future reuse must be intentional, legally compatible, attributed, maintainable, and justified by a separate implementation decision.

## Consequences

### Positive

- Servarr users get a familiar operational model.
- Nodearr can integrate with existing automation without replacing Sonarr or Radarr.
- Compatibility work is concentrated at explicit indexer and download-client boundaries.
- Peer trust, catalog availability, matching, and transfer verification remain first-class Nodearr concerns.
- Transfer providers can evolve without changing the Arr-facing product contract.

### Negative / trade-offs

- Nodearr must maintain both native and compatibility APIs.
- The product has to model catalog, peer trust, candidate matching, and transfer jobs before the full end-to-end flow works.
- Compatibility behavior must be tested against real Arr expectations.
- Familiarity with Arr applications can tempt accidental inheritance of unrelated media-manager assumptions.

## Alternatives considered

### Standalone P2P transfer app without Arr compatibility

Rejected. The core user value is integration into existing Arr automation workflows.

### Full Sonarr/Radarr-compatible application API

Rejected. Nodearr is not the source of truth for media libraries and should not pretend to be a full Arr application.

### Download client only

Rejected. A download-client-only interface does not solve discovery/search and would force the user to find peer candidates through some other mechanism.

### Indexer only

Rejected. An indexer-only interface can expose candidates but cannot own transfer authorization, progress, verification, failure state, and completed-file handoff.

## Follow-up work

- ~~Define native API resource boundaries.~~ Done: the v5 API, ADR 0003.
- ~~Define compatibility behavior for Sonarr and Radarr.~~ Done: ADR 0010.
- ~~Define transfer job states.~~ Done: ADR 0016.
- ~~Define the first local/simple transfer provider before replacing it with real P2P transport.~~ Not needed: the S2 spike went straight to iroh (ADR 0011, ADR 0012).


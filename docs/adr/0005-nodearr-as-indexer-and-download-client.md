# ADR 0005: Nodearr as Both Indexer and Download Client

## Status

Accepted.

## Context

Arr applications separate discovery from retrieval. Indexers expose candidates. Download clients own job state, retrieval progress, failures, and completed-file handoff.

Nodearr needs both roles because peer availability discovery and trusted peer transfer are part of the same product flow.

## Decision

Nodearr will act as both:

1. **Indexer**: expose trusted peer candidates through a Torznab/Newznab-compatible API.
2. **Download client**: accept grabbed candidates as transfer jobs, retrieve and verify bytes, expose job state, and provide an import-ready completed path.

The indexer role answers: **Who has an acceptable candidate?**

The download-client role answers: **How is that candidate being retrieved, verified, and handed back to the Arr workflow?**

The transfer provider is an internal Nodearr implementation detail. It should not be exposed to Arr apps as peer internals, transport addresses, or raw P2P protocol details.

## Consequences

### Positive

- The full Arr search/grab/import workflow can remain familiar.
- Nodearr owns transfer authorization, progress, verification, and failure reporting.
- Users do not need a separate tool to convert peer candidates into local files.
- Transfer providers can evolve without changing the indexer result contract.

### Negative / trade-offs

- Nodearr has to implement and test two compatibility surfaces.
- Queue and history behavior must be clear enough for Arr apps to monitor.
- Error mapping matters because connectivity, authorization, verification, and disk failures are different user-facing states.

## Alternatives considered

### Indexer-only Nodearr

Rejected. Search results alone do not retrieve trusted peer files or provide import-ready completed downloads.

### Download-client-only Nodearr

Rejected. It would require another discovery mechanism and would hide Nodearr's peer candidate model from Arr search.

### Return magnet links or raw peer URLs

Rejected as the core model. That would push transfer policy, trust, verification, and retry state outside Nodearr.

## Follow-up work

- Define transfer job lifecycle states.
- Define the first download-client-compatible queue/status endpoints.
- Define completed path behavior and import handoff.
- Define how failed transfers are represented to Arr apps and users.


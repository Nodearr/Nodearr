# ADR 0004: Technology Stack

## Status

Accepted.

## Context

Nodearr is intended to integrate naturally with the Servarr ecosystem and feel familiar to existing Arr users and contributors.

Architecture reconnaissance across Arr projects showed a common product structure:

- C#/.NET backend.
- ASP.NET Core host.
- Versioned APIs.
- SignalR for real-time UI updates.
- Swagger/OpenAPI for API documentation.
- React/TypeScript frontend.
- Provider-style integration abstractions.
- Self-hosted daemon/runtime packaging conventions.

Nodearr's accepted product shape requires:

- A self-hosted daemon/service.
- A local web UI.
- A native API.
- A Torznab/Newznab-compatible indexer boundary.
- An Arr-compatible download-client boundary.
- Transfer/provider abstractions.
- Health, logs, settings, and activity/queue visibility.

## Decision

Nodearr will use the same general stack shape as the Arr applications on purpose.

Initial stack:

- Backend: C# / .NET.
- Web host: ASP.NET Core.
- API style: versioned API routes using MVC controllers or minimal APIs where appropriate.
- Real-time updates: SignalR.
- API docs: OpenAPI/Swagger.
- Logging: NLog.
- Frontend: React + TypeScript.
- Frontend build: use a modern build tool while preserving Arr-familiar UI conventions.
- Database: SQLite for initial local persistence.
- Packaging: Docker-first, with config/data/download volumes planned from the beginning.
- License: GPL-3.0.

Nodearr should preserve its own domain model and naming even where it adopts familiar Arr patterns.

## Consequences

### Positive

- Easier conceptual alignment with Sonarr, Radarr, Prowlarr, and related Arr applications.
- Familiar contribution path for Servarr-adjacent developers.
- Natural fit for API-key auth, provider models, SignalR updates, and self-hosted daemon packaging.
- Reduces avoidable invention in host/API/UI layers.

### Negative / trade-offs

- Some inherited Arr conventions may be historical rather than ideal.
- The codebase must avoid dragging in unrelated media-manager assumptions.
- Direct peer-to-peer transport may eventually require libraries outside the normal Arr stack.
- Provider abstractions can be overbuilt if introduced before the domain model is stable.

## Alternatives considered

### Kotlin/Ktor + React

Rejected. Technically viable, but weaker ecosystem alignment.

### Go + React

Rejected. Good for small services and networking code, but weaker alignment with Servarr architecture and UI/API conventions.

### Rust + React

Rejected for the initial application stack. Rust may still be useful for transfer-provider experiments later, but it is not the best fit for the first full application skeleton.

## Follow-up work

- Continue shaping project names and layering around Nodearr domains.
- Add Docker-first development workflow.
- Add initial SQLite persistence boundary.
- Define transfer-provider integration points.
- Validate Iroh integration feasibility behind a Nodearr abstraction.

## Amendments

- 2026-10-10: Logging is NLog. This ADR originally left the choice between NLog and Serilog open. The host and core that Nodearr inherits from Sonarr already log through NLog (5.5.x) in every project, so the decision now records what the code does. See [NOTICE.md](../../NOTICE.md) for the reused Sonarr code.


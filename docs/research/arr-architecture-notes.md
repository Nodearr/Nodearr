# Arr Architecture Reconnaissance Notes

> Research from before Nodearr was built, kept as it was. What Nodearr took from these projects, and how, is in [NOTICE.md](../../NOTICE.md) and the [decision records](../adr/).

Date: 2026-05-21

Scope:

- `arr_checkouts/Sonarr`
- `arr_checkouts/Radarr`
- `arr_checkouts/Whisparr`

These repositories were inspected for architecture and product research. Nodearr may lift, adapt, or fork implementation code from these projects when licensing, attribution, maintainability, and domain fit are explicitly considered. The purpose of this document is to identify ecosystem conventions, compatibility surfaces, and reusable implementation areas that Nodearr can adopt deliberately.

## Executive summary

[Sonarr](https://sonarr.tv/), [Radarr](https://radarr.video/), and [Whisparr](https://whisparr.com/) share a common [Servarr](https://wiki.servarr.com/) lineage. They are self-hosted web applications with:

- A [.NET](https://dotnet.microsoft.com/) backend using ASP.NET Core hosting, MVC controllers, SignalR, OpenAPI/Swagger, background command/event patterns, and a historical `NzbDrone.*` project namespace.
- Product-specific API and HTTP projects layered over shared core/common/host projects.
- A provider/plugin model for indexers, download clients, notifications, import lists, metadata providers, and related integrations.
- A React/TypeScript frontend compiled separately and served by the backend as a single-page application.
- XML file configuration for process/server/auth settings and database-backed configuration for application settings.
- API-key based machine access, form/cookie based UI auth, optional no/external auth modes, URL-base support, and reverse-proxy-friendly headers.

For Nodearr, the most important adoptions are the product shape, compatibility surfaces, and reusable implementation patterns:

1. A self-hosted daemon with a local web UI.
2. A stable machine API guarded by API key.
3. A Torznab/Newznab-compatible indexer/feed boundary for Servarr apps.
4. Provider-style abstractions for pluggable transfer backends.
5. Arr-like UI information architecture: Dashboard/Library or Catalog, Activity/Queue, Settings, System, forms, tables, modals, health/status, logs, tasks.
6. Durable configuration, migrations, and operational packaging.

## 1. Backend framework and application structure

### Common framework

All three projects use C#/.NET with ASP.NET Core:

- Generic host and Kestrel startup are centered in `src/NzbDrone.Host/Bootstrap.cs`.
- ASP.NET Core service and middleware registration are centered in `src/NzbDrone.Host/Startup.cs`.
- MVC controllers are registered as services and assembled from product-specific API/HTTP assemblies.
- SignalR is used for real-time UI updates.
- Swagger/OpenAPI is generated for API versions.
- NLog is used for logging.
- DryIoc is used as the dependency injection container.

Representative inspected paths:

- `arr_checkouts/Sonarr/src/NzbDrone.Host/Bootstrap.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Host/Startup.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Common/Composition/Extensions.cs`
- `arr_checkouts/Radarr/src/NzbDrone.Host/Bootstrap.cs`
- `arr_checkouts/Radarr/src/NzbDrone.Host/Startup.cs`
- `arr_checkouts/Whisparr/src/NzbDrone.Host/Bootstrap.cs`
- `arr_checkouts/Whisparr/src/NzbDrone.Host/Startup.cs`

### Host lifecycle

The host layer handles:

- Startup context and command-line options.
- Interactive/service/utility modes.
- Binding address, HTTP port, SSL port, certificate loading, URL construction.
- Single-instance enforcement.
- Config file loading.
- Database/log database initialization.
- App startup events.
- Reverse proxy middleware, URL base, auth, CORS, compression, static frontend, SignalR, and controllers.

Nodearr implication: Nodearr should have a clear runtime host boundary responsible for process concerns, while catalog, peer, identity, and transfer logic live in domain/application layers.

## 2. Projects, solutions, and package organization

### Top-level shape

The three checkouts have similar top-level structure:

- `src/` for backend projects and solution files.
- `frontend/` for React/TypeScript SPA source.
- `distribution/` for packaging and runtime artifacts.
- `docker/` mostly for test/build artifacts in the inspected repositories.
- root `package.json`, `yarn.lock`, `tsconfig.json`, and frontend build configuration.

Examples:

- `arr_checkouts/Sonarr/src/Sonarr.sln`
- `arr_checkouts/Radarr/src/Radarr.sln`
- `arr_checkouts/Whisparr/src/Whisparr.sln`

### Backend project families

The `src/` layout includes shared historical projects and product-specific projects.

Common shared-style projects include:

- `NzbDrone.Common`
- `NzbDrone.Core`
- `NzbDrone.Host`
- `NzbDrone.Console`
- `NzbDrone.SignalR`
- `NzbDrone.Update`
- Platform projects such as `NzbDrone.Mono` and `NzbDrone.Windows`
- Test projects such as `NzbDrone.Core.Test`, `NzbDrone.Host.Test`, and integration/automation tests

These projects function as shared modules within each Arr application's repository/solution. The naming is historical, but the pattern is important: a large amount of host, common, core, datastore, provider, update, logging, and platform behavior is organized under reusable `NzbDrone.*` projects, while each product layers its own API/HTTP project and domain-specific behavior on top.

Across Sonarr, Radarr, and Whisparr, the repositories appear to carry similar shared module families rather than depending on a single external monorepo package at runtime. In practice, this means there is a common lineage and many parallel/shared-style modules, but each application checkout contains its own copy/version of those modules.

Product-specific API/HTTP projects include:

- Sonarr: `Sonarr.Api.V3`, `Sonarr.Api.V5`, `Sonarr.Http`
- Radarr: `Radarr.Api.V3`, `Radarr.Http`
- Whisparr: `Whisparr.Api.V3`, `Whisparr.Http`

Nodearr implication: Nodearr should separate host/runtime, domain/core, application services, API boundary, and UI. It may reuse or adapt shared-style Arr modules, but should rename and reshape them where needed so `NzbDrone` history does not obscure Nodearr-specific concepts.

## 3. APIs, controllers, and routes

### Versioned API routes

API controllers use product-specific attributes to define route templates:

- `arr_checkouts/Sonarr/src/Sonarr.Http/VersionedApiControllerAttribute.cs`
- `arr_checkouts/Sonarr/src/Sonarr.Http/VersionedFeedControllerAttribute.cs`

The inspected pattern is:

- JSON API routes under `api/v{version}/{resource}`.
- Feed routes under `feed/v{version}/{resource}`.
- Controllers decorated with `V3ApiController`, `V5ApiController`, or feed equivalents.
- Swagger doc inclusion based on controller version attributes.

### Controller conventions

Controllers commonly expose:

- RESTful CRUD endpoints.
- Bulk update/delete endpoints.
- Schema/template endpoints.
- Test and test-all endpoints for providers.
- Action endpoints for provider-specific dynamic operations.
- Paged list endpoints for large datasets.
- System endpoints for status, routes, tasks, backup, shutdown/restart, health, and logs.

Representative inspected paths:

- `arr_checkouts/Sonarr/src/Sonarr.Api.V3/ProviderControllerBase.cs`
- `arr_checkouts/Sonarr/src/Sonarr.Api.V3/System/SystemController.cs`
- `arr_checkouts/Sonarr/src/Sonarr.Api.V3/Indexers/ReleaseController.cs`
- `arr_checkouts/Sonarr/src/Sonarr.Api.V3/Queue/QueueController.cs`
- `arr_checkouts/Sonarr/src/Sonarr.Api.V3/Commands/CommandController.cs`

Nodearr implication: Nodearr should version its internal JSON API from the start, but the compatibility feed/API should be treated as an external contract. The first compatibility target should be Torznab/Newznab-style indexer search and grab semantics, not full Sonarr/Radarr API imitation.

## 4. Indexer modeling

### Provider foundation

Indexers are modeled as providers using a common abstraction sometimes known in the codebase as `ThingiProvider`.

Representative inspected paths:

- `arr_checkouts/Sonarr/src/NzbDrone.Core/ThingiProvider/ProviderDefinition.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/ThingiProvider/IProviderConfig.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/ThingiProvider/ProviderFactory.cs`
- `arr_checkouts/Sonarr/src/Sonarr.Api.V3/ProviderControllerBase.cs`

Provider definitions carry concepts such as:

- Name.
- Implementation type identifier.
- Config contract name.
- Enable/disable status.
- Tags.
- Provider message/status.
- Polymorphic settings object.

Provider factories handle:

- Default definitions and schema generation.
- Presets.
- CRUD.
- Resolving provider instances from configured definitions.
- Provider testing.
- Provider action dispatch.
- Removing stored definitions for missing implementations.

### Indexer-specific shape

Representative inspected paths:

- `arr_checkouts/Sonarr/src/NzbDrone.Core/Indexers/IndexerDefinition.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Indexers/IIndexer.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Indexers/Newznab/NewznabSettings.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Indexers/Torznab/TorznabSettings.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Indexers/Newznab/NewznabCapabilities.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Indexers/Newznab/NewznabCapabilitiesProvider.cs`
- `arr_checkouts/Sonarr/src/Sonarr.Api.V3/Indexers/IndexerController.cs`

Indexer definitions include:

- Download protocol.
- Support flags for RSS and search.
- Enable flags for RSS, automatic search, and interactive search.
- Optional download-client association.
- Priority.
- Status.

Release-like search results include concepts such as:

- GUID.
- Title.
- Size.
- Download URL and info URL.
- Indexer name and priority.
- Protocol.
- External media IDs.
- Publish date and age.
- Parsed source/container/codec/resolution metadata.
- Languages and flags.

Nodearr implication: Nodearr's "release candidate" should be a Nodearr-owned domain concept that maps cleanly to these expected indexer result fields. Media identity should come from configured Arr applications; content hashes should identify exact bytes for verification and dedupe. Release titles are presentation/search artifacts, not identity.

## 5. Download client modeling

### Download clients as providers

Download clients also use the provider abstraction.

Representative inspected paths:

- `arr_checkouts/Sonarr/src/NzbDrone.Core/Download/DownloadClientDefinition.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Download/IDownloadClient.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Download/DownloadClientItem.cs`
- `arr_checkouts/Sonarr/src/Sonarr.Api.V3/DownloadClient/DownloadClientController.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Download/DownloadService.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Download/DownloadProcessingService.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Download/CompletedDownloadService.cs`

Download client definitions include:

- Protocol.
- Priority.
- Remove-completed and remove-failed settings.
- Provider settings.

Download client operations include:

- Download/grab a selected release.
- List current client items.
- Derive import item/output path.
- Remove items.
- Get status.
- Mark item as imported.

Nodearr implication: Nodearr should appear as both an indexer and a download client from the Arr ecosystem perspective. The indexer surface discovers peer candidates, while the download-client surface exposes transfer job state and completed-file handoff. Internally, transfer providers should resemble a provider/plugin concept, but they should model peer-to-peer byte movement, not SABnzbd/qBittorrent-style client orchestration.

## 6. API keys and authentication

### Machine API auth

All three apps use API key authentication for machine/API access. The common patterns include:

- Header key: `X-Api-Key`.
- Query parameter key: `apikey`.
- Bearer token fallback in some handlers.
- Separate SignalR API-key scheme using `access_token` for query-string auth.
- Swagger/OpenAPI security definitions for header and query API key access.

Representative inspected paths:

- `arr_checkouts/Sonarr/src/NzbDrone.Host/Startup.cs`
- `arr_checkouts/Radarr/src/Radarr.Http/Authentication/AuthenticationBuilderExtensions.cs`
- `arr_checkouts/Radarr/src/Radarr.Http/Authentication/ApiKeyAuthenticationHandler.cs`
- `arr_checkouts/Whisparr/src/Whisparr.Http/Authentication/AuthenticationBuilderExtensions.cs`

### UI auth

The apps support forms/cookie auth, no auth, and external auth modes. Cookie naming is derived from instance name to avoid collisions.

### API key persistence

API keys are generated and stored in process config. Sonarr’s inspected `ConfigFileProvider` generates an API key when missing and supports reset via command.

Representative inspected path:

- `arr_checkouts/Sonarr/src/NzbDrone.Core/Configuration/ConfigFileProvider.cs`

Nodearr implication: Nodearr should support `X-Api-Key` and `apikey` for Servarr compatibility. Peer-to-peer node trust must not reuse the UI/API key model; peer identity and transport auth need separate cryptographic trust material.

## 7. Frontend structure

### Build and dependencies

All three apps keep SPA source under `frontend/`. Sonarr is the most modern inspected frontend reference.

Representative inspected paths:

- `arr_checkouts/Sonarr/package.json`
- `arr_checkouts/Sonarr/frontend/build/webpack.config.js`
- `arr_checkouts/Sonarr/frontend/src/App/App.tsx`
- `arr_checkouts/Sonarr/frontend/src/App/AppRoutes.tsx`
- `arr_checkouts/Sonarr/frontend/src/Components/`

The frontend uses:

- [React](https://react.dev/).
- [TypeScript](https://www.typescriptlang.org/).
- Webpack.
- CSS Modules and generated CSS typings.
- [React](https://react.dev/) Router.
- React Query in newer Sonarr code.
- Redux and other historical state utilities.
- SignalR client.
- FontAwesome icons.

### Frontend dependency and file reuse observations

The frontends do not appear to consume a single published shared Servarr UI package. Instead, like the backend, each product repository carries a similar frontend application tree with many shared-style directories and parallel files.

Observed package/dependency overlap:

- Radarr and Whisparr have very similar `package.json` dependency sets and versions, including React 18, React Router 5, Redux, React Query, SignalR, FontAwesome 6, Webpack 5, Babel, CSS Modules, PostCSS, Stylelint, and ESLint tooling.
- Sonarr uses the same broad toolchain and many of the same libraries, but appears more recently updated in several areas, including React Router 7, FontAwesome 7, SignalR 10, `@floating-ui/react`, `zustand`, newer Webpack/Babel versions, and newer React typings.
- All three build with root-level `package.json` scripts that call `frontend/build/webpack.config.js` and lint/stylelint the `frontend/` tree.

Observed same-file / near-same-file patterns:

- `frontend/build/webpack.config.js` is substantially the same across Sonarr, Radarr, and Whisparr: same Webpack structure, source/output folder assumptions, CSS Modules setup, Babel loader wiring, static asset copy behavior, LiveReload, MiniCssExtractPlugin, HtmlWebpackPlugin, FileManagerPlugin, and profiling branch. Sonarr has minor differences such as `publicPath: 'auto'`; Whisparr has a slightly smaller static copy section.
- `frontend/babel.config.js` is identical between Sonarr and Whisparr in the inspected checkouts and follows the same Babel plugin/preset shape used by the other Arr frontends.
- Top-level SPA entry files and static shells are shared-style across products: `bootstrap.tsx`, `index.ts`, `index.css`, `index.ejs`, `login.html`, `oauth.html`, and `polyfills.js`.
- The `frontend/src` layout repeats many cross-product folders: `Activity`, `App`, `Calendar`, `Commands`, `Components`, `Content`, `Diag`, `DownloadClient`, `FirstRun`, `Helpers`, `InteractiveImport`, `InteractiveSearch`, `Language`, `Organize`, `Parse`, `Quality`, `RootFolder`, `Settings`, `Shared`, `Store`, `Styles`, `System`, `Tags`, `typings`, `Utilities`, and `Wanted`.
- Product-specific folders sit alongside those shared-style folders: Sonarr has `Series`, `Episode`, `Season`, and related TV concepts; Radarr has `Movie`, `Collection`, and discovery/movie-file concepts; Whisparr has `Movie`, `Performer`, `Scene`, `Studio`, `UnmappedFiles`, and related adult-library concepts.

Nodearr implication: the frontend is a strong candidate for direct reuse or forking. The highest-value areas to lift first are likely build configuration, global styling conventions, app shell/layout, navigation, `Components/`, `Shared/`, `Store/`, `Utilities/`, modal/table/form primitives, provider settings UI, activity/queue pages, system/log/status pages, and static login/OAuth shells. Nodearr-specific domains such as peers, catalog, release candidates, and transfers can be introduced as new feature folders next to the reused shared-style structure.

### Source organization

Sonarr’s `frontend/src` includes feature folders such as:

- `Activity/`
- `Calendar/`
- `Commands/`
- `Components/`
- `DownloadClient/`
- `InteractiveSearch/`
- `Settings/`
- `System/`
- `Wanted/`

Reusable UI primitives are grouped under `Components/`, including:

- `Form/`
- `Modal/`
- `Page/`
- `Table/`
- `Menu/`
- `Link/`
- `Filter/`
- `Loading/`
- `Tooltip/`

Nodearr implication: Nodearr should adopt comparable information architecture and reusable UI primitives. Component implementations, styling conventions, and assets may be lifted or adapted where licensing and maintainability are acceptable, especially to make Nodearr feel familiar to existing Arr users.

## 8. UI conventions that make an app feel like Arr

Observed Arr UI conventions:

- Left/app navigation organized around domain pages, Activity, Settings, and System.
- Page title and toolbar at top of content area.
- Toolbar sections with icon buttons and right-aligned options/filter controls.
- Data-heavy tables with column visibility, sorting, paging, filtering, select-all, and row actions.
- Modal-driven create/edit/test flows.
- Provider settings pages with cards/list entries, schema-backed forms, advanced fields, test buttons, and validation warnings.
- System pages for status, tasks, backups, updates, events, and logs.
- Health checks surfaced prominently.
- Queue/activity pages for in-progress and historical operations.
- Consistent iconography, badges/labels, progress bars, spinners, confirmation modals, and translated strings.
- URL base support for reverse proxy hosting.
- Real-time updates via SignalR.

Representative inspected paths:

- `arr_checkouts/Sonarr/frontend/src/App/AppRoutes.tsx`
- `arr_checkouts/Sonarr/frontend/src/Wanted/Missing/Missing.tsx`
- `arr_checkouts/Sonarr/frontend/src/InteractiveSearch/InteractiveSearch.tsx`
- `arr_checkouts/Sonarr/frontend/src/Settings/Indexers/`
- `arr_checkouts/Sonarr/frontend/src/Settings/DownloadClients/`
- `arr_checkouts/Sonarr/frontend/src/Components/Page/`
- `arr_checkouts/Sonarr/frontend/src/Components/Table/`
- `arr_checkouts/Sonarr/frontend/src/Components/Modal/`
- `arr_checkouts/Sonarr/frontend/src/Components/Form/`

Nodearr implication: Nodearr should intentionally feel familiar to Servarr users by matching interaction patterns and, where useful, reusing or adapting UI code and styling. Early Nodearr UI pages should likely be Catalog, Peers, Activity/Transfers, Settings, and System.

## 9. Configuration persistence

### Process/config-file settings

The apps use XML config files for server/process/auth/update/logging settings. Sonarr’s `ConfigFileProvider` demonstrates:

- XML config document with a single root config element.
- Typed property accessors with defaults.
- Persisting defaults when needed.
- Environment/options overrides for some settings.
- API key generation when missing.
- URL base, ports, SSL, auth method, instance name, branch, logging settings.
- Startup migration and cleanup of old values.

Representative inspected paths:

- `arr_checkouts/Sonarr/src/NzbDrone.Host/Bootstrap.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Configuration/ConfigFileProvider.cs`

### Database-backed app settings

Application settings are persisted through configuration services/repositories in the main database.

Representative inspected paths:

- `arr_checkouts/Sonarr/src/NzbDrone.Core/Configuration/ConfigService.cs`
- `arr_checkouts/Sonarr/src/NzbDrone.Core/Datastore/`

Nodearr implication: Nodearr should separate boot-critical settings from runtime application settings. Sensitive trust material and peer keys deserve explicit storage design rather than being mixed into generic UI settings.

## 10. Docker and runtime artifacts

The inspected repositories include runtime packaging artifacts, systemd units, platform-specific packaging, and [Docker](https://docs.docker.com/) build/test support.

Representative inspected paths:

- `arr_checkouts/Sonarr/distribution/debian/sonarr.service`
- `arr_checkouts/Sonarr/distribution/docker-build/Dockerfile`
- `arr_checkouts/Sonarr/distribution/docker-build/debian-start.sh`
- `arr_checkouts/Sonarr/docker/tests/`
- `arr_checkouts/Radarr/distribution/`
- `arr_checkouts/Whisparr/distribution/`

Notable runtime conventions:

- Systemd service runs the app with `-nobrowser` and `-data=<app data path>`.
- Dedicated service user/group and umask.
- Data/config path is separate from application binaries.
- Docker/build artifacts distinguish build inputs, output, and application binaries.

Nodearr implication: Nodearr should plan container-first deployment with explicit volumes for config/database, completed downloads, catalog cache, and logs. It should also support a direct binary/service mode later.

## 11. Concepts Nodearr should adopt or reuse deliberately

Nodearr should adopt these concepts at the architecture/product level and consider lifting/adapting implementation pieces where doing so is worthwhile:

1. **Self-hosted daemon + local web UI**: familiar operational model for Servarr users.
2. **Stable versioned API**: native JSON API under a Nodearr-owned versioned namespace.
3. **Torznab/Newznab-compatible boundary**: compatibility surface for Servarr search and grab flows.
4. **API key compatibility**: `X-Api-Key` and `apikey` for machine access.
5. **Provider abstraction**: use plugin-like definitions/settings/test/status for transfer providers and possibly metadata sources.
6. **Schema-backed settings UX**: settings forms should be describable enough for dynamic UI generation or consistent typed rendering.
7. **Activity and queue model**: transfer jobs should have explicit status, progress, history, retry/failure state, and logs.
8. **Health checks and system visibility**: peer reachability, catalog freshness, path validity, transfer provider health, disk space, and API status.
9. **URL base/reverse proxy support**: important for self-hosting.
10. **Durable configuration and migration discipline**: boot config, app settings, and secrets/trust material should have clear persistence boundaries.
11. **Arr-like UI conventions**: tables, filters, modals, toolbar actions, provider cards, settings pages, task/log/status pages.
12. **Real-time updates**: transfer progress and peer/catalog changes should update the UI live.

## 12. Implementation reuse guidance for Nodearr

Nodearr should treat reuse as a deliberate engineering decision. Good candidates for lifting, adapting, or forking include UI layout primitives, provider/settings form patterns, table/modal/filter interactions, API authentication conventions, feed/indexer response handling, health/status/logging patterns, and packaging conventions. Reuse should account for license obligations, attribution, update strategy, and the amount of product-specific coupling.

Nodearr should avoid:

1. Blind copy/paste without understanding license, dependencies, ownership, or long-term divergence.
2. Reusing `NzbDrone` namespaces, product names, historical project layout, or internal abstractions verbatim when they obscure Nodearr's domain model.
3. Treating filenames as file identity. Media identity must come from Arr metadata; content hashes should verify exact bytes.
4. Modeling peer transfer as a traditional external download client without considering trust, availability, multi-peer sources, resumability, and content verification.
5. Mixing peer trust credentials with local UI/API keys.
6. Pretending to be a full Sonarr/Radarr/Whisparr API. Compatibility should be deliberate and limited to needed Servarr-facing boundaries.
7. Adopting XML config solely because Arr apps use it. Nodearr should choose persistence format later based on its own stack and operational needs.
8. Locking into a final tech stack in this research pass.
9. Diverging from Arr UI conventions unnecessarily. Nodearr should look and behave similarly enough that existing Arr users feel at home.
10. Implementing provider/schema magic prematurely before Nodearr’s core domain model is stable.

## Initial Nodearr architecture implications

Nodearr should be designed around these primary domains:

- **Node/Peer Trust**: trusted peers, invitations, node identity, connectivity, permissions.
- **Catalog Sources**: configured Arr applications, starting with Sonarr and Radarr.
- **Catalog**: local Arr-imported availability and peer-known availability.
- **Media Identity**: Arr-provided external IDs, titles, item relationships, quality/profile data, and file paths.
- **File Identity**: content hash, chunk hashes, size, and verification status for exact bytes.
- **Release Candidate**: Servarr-searchable projection of available catalog items.
- **Transfer Job**: local retrieval request, progress, state, destination, verification.
- **Transfer Provider**: pluggable backend for moving bytes from trusted peers.
- **Compatibility API**: Torznab/Newznab-style feed/search/grab boundary.
- **Native API/UI**: first-party Nodearr management surface.

## Open questions for later phases

- Final backend/frontend tech stack.
- Exact Arr-compatible download-client API surface and job-state mapping.
- Exact peer transport design and NAT traversal assumptions.
- Content hash algorithm and chunking strategy.
- Catalog sync protocol and conflict/staleness semantics.
- Matching strategy from Sonarr and Radarr first, then future source contracts for music, books, adult media, and other Arr-like applications.
- How release candidate quality/source/codec inference should work without depending on filenames.
- Security model for private groups, peer revocation, and per-peer visibility permissions.

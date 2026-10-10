# Inherited Sonarr features

Written 2026-10-10 for slice S0 of the [V1 implementation plan](../plans/v1-implementation-plan.md), against branch `v1/s0-baseline`.

Nodearr's backend and frontend were ported from Sonarr `v5-develop` (see [NOTICE.md](../../NOTICE.md)), so the repository still contains a complete TV library manager. This document lists what was inherited and what V1 should do with each part. The plan's rule is to remove inherited features only where they get in the way, so most of this list is "leave it alone".

Each item has one of four dispositions:

| Disposition | Meaning |
| --- | --- |
| Keep | Infrastructure Nodearr uses as is. |
| Reshape | Useful, but needs changing for Nodearr's domain. The slice that does it is named. |
| Unused | Not needed by V1 and harmless. Leave it until it gets in the way. |
| In the way | Breaks, misleads or collides with V1 work. Needs action or an owner decision. |

## In the way

These are the items that need attention. Everything else in this document can wait.

| Item | What happens | Action |
| --- | --- | --- |
| Cover image resizing (SixLabors.ImageSharp 3.1.12) | Five advisories published 2026-10-07 made every project fail restore, because warnings are errors. The only patched release is 4.1.2 or later, and 4.x runs a license-key check during Release builds. | Done in S0: the resizer and the package were removed. Covers are still downloaded, and both `MediaCoverController` and `MediaCoverMapper` already fall back to the full-size image when a resized one is missing. |
| Microsoft.OpenApi 2.4.1 (via Swashbuckle 10.1.7) | A high-severity advisory failed restore in the same way. | Done in S0: Swashbuckle bumped to 10.2.3, which depends on the patched Microsoft.OpenApi 2.7.5. |
| Default port 8989 | It is Sonarr's default port, so Nodearr and Sonarr collided on one host. | Done in S3: the default is 6633, which spells NODE on a phone keypad. |
| Renamed cloud endpoints | The rename turned Sonarr's service hosts into `services.nodearr.com`, `skyhook.nodearr.com`, `sentry.nodearr.com` and `download.nodearr.com`. None of them resolve today. On every start `ServerSideNotificationService` logs "Failed to retrieve notifications" and `SystemTimeCheck` logs "Unable to verify system time". Sentry crash reporting is still registered with Sonarr's DSN keys against the renamed host, and analytics defaults to enabled. The project owner holds `nodearr.com`, so nothing can answer on those names unless the project sets it up. | Owner decision before any release: turn off Sentry, analytics, the update check, server-side notifications, the system time check and scene mapping updates, or host replacements for the ones worth keeping. |
| Always-failing health checks | A fresh install shows two errors and two warnings on System > Status: `IndexerRssCheck`, `IndexerSearchCheck`, `DownloadClientCheck` and `UpdateCheck`. They describe Sonarr's need for indexers and download clients, which Nodearr does not have. | Done in S3 for `IndexerRssCheck`, `IndexerSearchCheck` and `DownloadClientCheck`, which are removed. `UpdateCheck` remains and fails on development builds only. |
| TV scheduled tasks | `RssSync` (15 min), `RefreshSeries` (12 h), `ImportListSync` (5 min), `UpdateSceneMapping` (3 h), `RefreshMonitoredDownloads` (1 min) and `ApplicationUpdateCheck` (6 h) are all scheduled. They do nothing useful with an empty library, and some call the dead cloud hosts. | Done in S3: all six are unscheduled. `CatalogRefresh` and `ApplicationIndexerSync` run every 6 hours instead. |
| Table names `Series`, `Episodes`, `EpisodeFiles` | The 223 inherited migrations create Sonarr's library tables. S3 needs its own normalized series, episode, episode-file and movie tables. | Done in S3: Nodearr's tables are `CatalogSeries`, `CatalogEpisodes`, `CatalogEpisodeFiles`, `CatalogMovies`, `CatalogMovieFiles` and `CatalogQualityProfiles`. Sonarr's tables are left in place, unused. |
| Copyright string | `src/Directory.Build.props` stamps assemblies with `Copyright 2014-<year> nodearr.com`, which no longer credits Sonarr. | Owner decision on wording. See [NOTICE.md](../../NOTICE.md). |

## Host and infrastructure

All of this is kept. It is the reason for reusing Sonarr.

| Feature | Where | Disposition |
| --- | --- | --- |
| Generic host, Kestrel, startup arguments (`-data`, `-nobrowser`), single-instance check | `NzbDrone.Host`, `NzbDrone.Console`, `NzbDrone.Common` | Keep |
| Windows tray app and service helpers | `NzbDrone` (`Nodearr.csproj`), `NzbDrone.Windows`, `ServiceHelpers` | Unused. Docker is the V1 target. |
| DryIoc container with assembly scanning | `NzbDrone.Common/Composition` | Keep |
| NLog logging, log files, log database, Sentry target | `NzbDrone.Common/Instrumentation` | Keep, except Sentry (in the way) |
| `config.xml` settings, API key generation, URL base, SSL | `NzbDrone.Core/Configuration` | Keep |
| SQLite and PostgreSQL through Dapper and FluentMigrator | `NzbDrone.Core/Datastore` | Keep. Nodearr migrations start at 231. |
| Command queue, scheduler, event aggregator | `NzbDrone.Core/Messaging`, `Jobs` | Keep. S3 and S7 build on these. |
| SignalR broadcasting | `NzbDrone.SignalR` | Keep. S7 uses it for live transfers. |
| API-key, forms and external authentication, first-run "Authentication Required" prompt | `Nodearr.Http/Authentication`, `NzbDrone.Core/Authentication` | Keep |
| Provider framework (definitions, schema, test, status, back-off) | `NzbDrone.Core/ThingiProvider` | Keep. Arr Apps already use it. |
| HTTP client, proxy support, rate limiting | `NzbDrone.Common/Http` | Keep |
| Backup, housekeeping, disk space, file system browser, tags, custom filters, localization | `NzbDrone.Core` | Keep |
| Health check framework | `NzbDrone.Core/HealthCheck` | Keep the framework. Individual checks are listed below. |
| Self-update | `NzbDrone.Update`, `NzbDrone.Core/Update` | Unused. Docker images replace it. The update check is in the way. |
| Analytics | `NzbDrone.Core/Analytics` | In the way. `AnalyticsEnabled` defaults to true. Decide it together with Sentry. |

## API surface

`GET /api` reports V5 as current and V3 as deprecated. Both are Sonarr's API almost unchanged: about 70 V3 controllers and about 64 V5 controllers. The only Nodearr-specific one is `api/v5/applications`.

| Feature | Disposition |
| --- | --- |
| `system/status`, `system/task`, `system/backup`, `health`, `log`, `command`, `tag`, `customfilter`, `filesystem`, `diskspace`, `localization`, `update`, settings controllers | Keep |
| `applications` (V5 only) | Reshape in S3 |
| `series`, `episode`, `episodefile`, `seasonpass`, `calendar`, `wanted/missing`, `wanted/cutoff`, `rename`, `manualimport`, `rootfolder`, `importlist`, `metadata`, `mediacover` | Unused |
| `indexer`, `downloadclient`, `release`, `release/push`, `queue`, `history`, `blocklist`, `remotepathmapping`, `parse` | Unused. These are Sonarr acting as a client of indexers and download clients. Nodearr is the server side of both. |
| `qualityprofile`, `qualitydefinition`, `customformat`, `releaseprofile`, `delayprofile`, `language`, `autotagging` | Unused in V1. Profile filtering is UI-only (plan decision 2). |
| Feed route `feed/v3/calendar/nodearr.ics` | Unused |
| V3 as a whole | Unused. Nothing in Nodearr's frontend needs the deprecated version, but removing it is not required for V1. |

Nodearr's own Arr-facing endpoints (Newznab indexer, SABnzbd-compatible API) do not exist yet. S1 adds them. They must not be confused with the inherited `Indexers` and `Download` namespaces, which implement the opposite side of the same protocols.

## TV library domain

| Feature | Where | Disposition |
| --- | --- | --- |
| Series, seasons, episodes, monitoring, series statistics | `NzbDrone.Core/Tv`, `SeriesStats` | Unused. Arr apps own media identity (ADR 0006). |
| Series metadata from SkyHook, scene mappings | `MetadataSource`, `DataAugmentation` | Unused. Calls a renamed host (in the way). |
| Episode files, disk scan, import, rename, recycle bin, extras, subtitles | `MediaFiles`, `Organizer`, `Extras` | Unused. Sonarr and Radarr do the importing. |
| Root folders | `RootFolders` | Unused |
| Media covers | `MediaCover` | Unused. Resizing removed in S0. |
| Metadata file writers (Kodi, Plex, Roksbox, WDTV) | `Extras/Metadata/Consumers` | Unused |
| Import lists (AniList, MyAnimeList, Plex, RSS, Simkl, Sonarr, Trakt, custom) | `ImportLists` | Unused |
| Auto tagging | `AutoTagging` | Unused |

## Search, grab and download pipeline

| Feature | Where | Disposition |
| --- | --- | --- |
| Indexer clients (Newznab, Torznab, BroadcastheNet, Fanzub, FileList, HDBits, IPTorrents, Nyaa, Torrentleech, TorrentRss) | `Indexers` | Unused. The Newznab parser and capabilities model are a useful reference for the response shapes S1 and S6 must produce. |
| Download client clients (SABnzbd, NZBGet, NzbVortex, qBittorrent, Deluge, Transmission, rTorrent, uTorrent, Vuze, Aria2, Flood, Hadouken, Download Station, Freebox, RQBit, Tribler, Blackhole, Pneumatic) | `Download/Clients` | Unused. The SABnzbd proxy and its response models show exactly what Sonarr expects from the API S1 emulates. |
| RSS sync, search, decision engine, delay and release profiles, pending releases | `IndexerSearch`, `DecisionEngine`, `Profiles`, `Download/Pending` | Unused |
| Queue, tracked downloads, completed and failed download handling, history, blocklist | `Queue`, `Download`, `History`, `Blocklisting` | Unused. Nodearr's transfer job model (S7) is separate. |
| Remote path mappings | `RemotePathMappings` | Reshape in S3, which needs path mappings between Arr and Nodearr. |
| Release title parser, qualities, languages, custom formats | `Parser`, `Qualities`, `Languages`, `CustomFormats` | Reshape in S6. Nodearr must emit release titles that Sonarr and Radarr parse correctly, and these are the rules they are parsed with. |
| Notifications (25 connections, including Discord, Email, Plex, Slack, Telegram, Webhook, custom script) | `Notifications` | Unused in V1. Their events are TV events. |
| Applications sync, ported from Prowlarr, Sonarr only | `Applications` | Reshape in S3: register one Nodearr indexer and one download client, add Radarr, and fetch catalogs. |

## Scheduled tasks

Read from `GET /api/v5/system/task` on a fresh install.

| Task | Interval | Disposition |
| --- | --- | --- |
| CheckHealth | 6 h | Keep |
| Housekeeping | 24 h | Keep |
| Backup | 7 days | Keep |
| MessagingCleanup | 5 min | Keep |
| CleanUpRecycleBin | 24 h | Unused |
| RefreshMonitoredDownloads | 1 min | Unscheduled in S3 |
| RssSync | 15 min | Unscheduled in S3 |
| RefreshSeries | 12 h | Unscheduled in S3 |
| ImportListSync | 5 min | Unscheduled in S3 |
| UpdateSceneMapping | 3 h | Unscheduled in S3 |
| ApplicationUpdateCheck | 6 h | Unscheduled in S3 |
| CatalogRefresh | 6 h | Added in S3 |
| ApplicationIndexerSync | 6 h | Added in S3 |

## Health checks

| Check | Disposition |
| --- | --- |
| ApiKeyValidationCheck, AppDataLocationCheck, MountCheck, ProxyCheck, PackageGlobalMessageCheck | Keep |
| SystemTimeCheck | In the way. Calls a renamed host. |
| UpdateCheck | In the way. Fails on a development build because the UI folder is not writable. |
| IndexerRssCheck, IndexerSearchCheck, DownloadClientCheck | Removed in S3. They always failed in Nodearr. |
| IndexerStatusCheck, IndexerLongTermStatusCheck, IndexerDownloadClientCheck, IndexerJackettAllCheck, DownloadClientStatusCheck, DownloadClientRootFolderCheck, DownloadClientSortingCheck, DownloadClientRemovesCompletedDownloadsCheck, ImportListStatusCheck, ImportListRootFolderCheck, ImportMechanismCheck, NotificationStatusCheck, RecyclingBinCheck, RemotePathMappingCheck, RemovedSeriesCheck, RootFolderCheck | Unused. Silent while their features are unconfigured. |

## Database

Migrations 1 to 230 are Sonarr's and create its whole schema. Migration 231 adds `Applications` and `ApplicationStatus`. Migration 232 adds the `Catalog*` tables and drops the unused `ApplicationIndexerMapping`.

| Tables | Disposition |
| --- | --- |
| `Config`, `ScheduledTasks`, `Commands`, `Users`, `Tags`, `CustomFilters`, `Logs`, `UpdateHistory` | Keep |
| `Applications`, `ApplicationStatus`, `Catalog*` | Nodearr's own, as of S3 |
| `RemotePathMappings` | Reused in S3 as Arr-path to Nodearr-path mappings, keyed by the Arr app's host |
| `Series`, `Episodes`, `EpisodeFiles`, `SceneMappings`, `RootFolders`, `NamingConfig`, `MetadataFiles`, `SubtitleFiles`, `ExtraFiles`, `Metadata`, `ImportLists`, `ImportListItems`, `ImportListStatus`, `ImportListExclusions` | Unused. The names collide with what S3 needs (in the way). |
| `Indexers`, `IndexerStatus`, `DownloadClients`, `DownloadClientStatus`, `History`, `DownloadHistory`, `Blocklist`, `PendingReleases`, `QualityDefinitions`, `QualityProfiles`, `CustomFormats`, `ReleaseProfiles`, `DelayProfiles`, `Notifications`, `NotificationStatus` | Unused |

## Frontend

The routes are already Nodearr's: Catalog (`/`, `/catalog/local`, `/catalog/peers`), Nodes (`/nodes`, `/nodes/trusted`, `/nodes/invites`), Activity (`/activity/transfers`), Settings (`/settings`, `/settings/apps`, `/settings/general`) and System (`/system/status`). Most are placeholders.

| Area | Where | Disposition |
| --- | --- | --- |
| App shell, sidebar, header, page, table, form, modal and menu components, styles, icons | `App`, `Components`, `Styles`, `Helpers`, `Utilities`, `Content` | Keep |
| Redux store, React Query hooks, SignalR listener | `Store`, `App`, `Helpers/Hooks` | Keep |
| System pages (Status, Tasks, Backup, Logs, Events, Updates) | `System` | Keep. Only Status is routed. |
| First-run authentication modal, OAuth hook | `FirstRun`, `OAuth` | Keep. Sonarr's `login.html` page was not ported, so forms login has no page yet. |
| Arr Apps settings (from Prowlarr) | `Settings/Applications` | Reshape in S3 |
| Nodearr placeholders | `Catalog`, `Nodes`, `Activity/Transfers` | Reshape in S3 to S7 |
| Series, seasons, episodes, calendar, wanted, add series, organize, interactive import and search, parse | `Series`, `Season`, `Episode`, `EpisodeFile`, `AddSeries`, `Calendar`, `Wanted`, `Organize`, `InteractiveImport`, `InteractiveSearch`, `Parse`, `RootFolder` | Unused. Not routed. Commit `841fd25` kept them on purpose so later removal is selective and never breaks the build. |
| Queue, History, Blocklist pages | `Activity` | Unused. Good models for the transfers page in S7. |
| Sonarr settings pages (Media Management, Profiles, Quality, Custom Formats, Indexers, Download Clients, Import Lists, Connect, Metadata, Metadata Source, Tags, General, UI) | `Settings` | Unused. General, UI and Tags are worth routing later. |

## Tests

About 540 inherited test files across `NzbDrone.*.Test` and `Nodearr.Http.Test`, mostly covering the unused TV features. They compile as part of the solution. Only the `MediaCoverTests` fixtures were run in S0 (8 passed). The full suite has not been run against this fork, and several fixtures call external hosts, so its state is unknown.

## Baseline verified in S0

On Windows 11 with .NET SDK 10.0.303, Node 20.20.2 and Yarn 4.15.0:

- `yarn build` compiles successfully into `_output/UI`.
- `dotnet build src/Nodearr.sln` succeeds with 0 warnings and 0 errors, after the two dependency fixes above.
- `dotnet run --project src/NzbDrone.Console/Nodearr.Console.csproj -- -nobrowser -data=<dir>` starts, runs migrations to 231 on a new SQLite database and listens on port 8989.
- `GET /ping` returns 200. `GET /api/v3/system/status` and `GET /api/v5/system/status` return 200 with the API key and 401 without it.
- `GET /api/v5/applications` returns an empty list and its schema lists one implementation, Sonarr.
- The UI loads with the Nodearr sidebar and the first-run authentication prompt, with no browser console errors.

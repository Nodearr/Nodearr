# AGENTS.md

Instructions for coding agents working on Nodearr. People start with [README.md](README.md) and [CONTRIBUTING.md](CONTRIBUTING.md). This file covers what an agent needs and tends to miss. Where CONTRIBUTING says more, it is the authority.

## What Nodearr is

Nodearr lets friends fetch media from each other's libraries, peer to peer. Each person's Arr apps ([Sonarr](https://sonarr.tv/), [Radarr](https://radarr.video/), Whisparr, Lidarr) use their Nodearr as a Newznab indexer and a SABnzbd download client. Nodearr reads its catalog from those apps, exchanges catalogs with trusted friends, and moves files over [iroh](https://www.iroh.computer/) through a Rust sidecar.

It is a fork of Sonarr: .NET 10 backend, React and TypeScript frontend, plus the Rust sidecar. A lot of inherited TV code is still present. Leave it alone unless the task is about it.

## Where things are

- `src/NzbDrone.Core/`: the backend. Nodearr's own code is in `Catalog/`, `PeerCatalog/`, `PeerIndexer/`, `Posters/`, `Sharing/`, `Nodes/` (identity, pairing, trust, `Nodes/Transport/IrohSidecarTransport.cs`) and `Transfers/`. Most other folders are inherited from Sonarr.
- `src/NzbDrone.Core/Datastore/Migration/`: numbered migrations. A new one takes the next free number.
- `src/NzbDrone.Core/Localization/Core/en.json`: UI strings. Add new strings here; don't hard-code them in components.
- `src/Nodearr.Http/`: the Newznab (`Newznab/`) and SABnzbd (`Sabnzbd/`) compatibility APIs that the Arr apps call.
- `src/Nodearr.Api.V5/`: the native API. `openapi.json` is generated; don't edit it by hand.
- `src/*.Test/`: NUnit tests, beside the code they test (`src/NzbDrone.Core.Test/Catalog/`, `…/Transfers/` and so on).
- `frontend/src/`: the UI. Nodearr's pages are mainly in `Catalog/`, `Nodes/`, `Home/`, `Activity/` and `Settings/`.
- `transport/`: the `nodearr-transport` sidecar. Its HTTP API to Nodearr and its options are in `main.rs`; connections and blocking in `connections.rs`; file transfer, the upload queue and rate limits in `transfer.rs`. The sidecar is deliberately opaque: it doesn't read Nodearr's messages, and Nodearr tells it how to handle each one.
- `docs/overview-end-to-end-design.md`: how it all fits together. Start here.
- `docs/adr/`: numbered decision records (NNNN-title.md). `docs/diagrams/`: flows in Mermaid. `docs/plans/v1-implementation-plan.md`: one line per slice, plus **What is still open**.
- `docs/user-guide/`: the user guide. It is also synced to the website ([Nodearr/nodearr.github.io](https://github.com/Nodearr/nodearr.github.io)).
- `acceptance/`: end-to-end PowerShell scripts that run against real Arr containers (see [acceptance/README.md](acceptance/README.md)).
- `build/`: release, OpenAPI, notices and test runners. `tools/`: the manual UI kit and docs screenshots.

## Design rules

These come from the ADRs. Read the ADR before changing the area it covers. A change of design starts as a new ADR, or as a dated amendment to an existing one, that says what changed and why.

- **The Arr apps are the source of truth** for the library (ADR 0006). Nodearr mirrors how Sonarr and Radarr model things, such as per-episode files and their quality and release conventions. It doesn't invent its own model alongside theirs.
- **Content moves only over a direct path** (ADR 0011). File data, catalogs, catalog changes, posters and sent titles all wait for a direct path and are never sent over a relay. Pairing, pings and revoking may cross a relay. If a direct path can't be made, the action fails clearly; it never falls back to a relay.
- **No calls home** (ADR 0018). Don't add telemetry, update checks or any outbound call that a user didn't configure.
- **A sender enforces its own limits.** Upload limits, the upload queue and sharing rules are enforced by the node that holds the files, never trusted to the receiver. An older or modified friend must fail safely, never get around a limit.
- **Older friends keep working.** A wire change must either stay compatible with nodes from earlier releases or fail safely with them. S7 tests this with `-OlderNode A|B -OlderTransportBin <path>` (see [acceptance/README.md](acceptance/README.md)). Say which older release was tested.
- **Settings** are saved in the UI and can be fixed by the environment (`Nodearr__Section__Key`) (ADR 0019).
- **The key exchange stays post-quantum** (ADR 0023). iroh is built with `tls-aws-lc-rs`, and `tls-ring` must stay off.

## Commands

Run these from the repository root. The repository's scripts need PowerShell 7 (`pwsh`). CI runs on Linux and Windows; macOS isn't tested there.

```sh
dotnet build src/Nodearr.sln              # warnings are errors, StyleCop included
npm test                                  # all .NET test assemblies, then the Rust tests, as in CI
cargo test --locked --manifest-path transport/Cargo.toml
cargo build --release --manifest-path transport/Cargo.toml
yarn install --immutable
yarn lint && yarn stylelint && yarn typecheck && yarn build --env production
pwsh build/Build-OpenApi.ps1              # after any API change; commit openapi.json
pwsh build/Build-Notices.ps1              # after any dependency change; commit the result
```

Run all four UI checks before committing a frontend change. CI runs `stylelint` too, and a failure there has blocked a merge before.

Acceptance scripts, `pwsh acceptance/Run-<name>.ps1`: run the ones for the area you changed.

- `S3CatalogImport`: registering with the Arr apps and importing the catalog
- `S4Pairing`: identity, invites, trust, blocking
- `S5Sharing`: sharing rules and catalog exchange
- `S6Indexer`: the Newznab indexer and matching
- `S7Transfers`: transfers, limits, the upload queue, revoking, older nodes
- `S8Docker`: the image deployed twice, with a self-hosted relay
- `CatalogGetIt`, `IdenticalCopies`, `NodeSettings`, `GeneralSettings`: the features they're named after

These scripts take minutes. Run them in the background and follow their output to the end. They need Docker and fixed ports: S7 uses 28989 and 27878 for its consumer Arr apps, which the UI kit also uses. If the kit is running, pass `-SonarrConsumerPort 38989 -RadarrConsumerPort 37878`. Test data goes under `acceptance/.run/` and is safe to delete.

To try a change by hand, use `pwsh tools/ui-test.ps1 up` (see [docs/guides/ui-test.md](docs/guides/ui-test.md)). This kit, and so the screenshots below, runs on Windows only, because it installs the win-x64 packages of Nodearr and the Arr apps. Always pass `-NodearrZip <path>`: without it, the kit installs the newest package in Downloads, which may be an old release. Build that package with `build/Build-Release.ps1`.

The user guide's screenshots aren't regenerated automatically: CI doesn't take them. Retake them yourself when a page they show changes. The steps and options are at the top of `tools/docs-screenshots.mjs`. What's easy to get wrong:

- Run the kit on a package built from your branch (`up -Solo -NodearrZip <path>`). The help texts in the shots come from the server, so a release package shows the old text.
- Run `node tools/docs-screenshots.mjs prepare`, and `prepareMusic` for the music shots, then capture everything or only the shots you name.
- Playwright and sharp aren't Nodearr dependencies. Install them elsewhere and point `NODE_PATH` at them. Sign-in comes from `NODEARR_USERNAME` and `NODEARR_PASSWORD`, and the kit's folder from `NODEARR_KIT`.
- The `send` shot changes the test data. Recreate the kit before taking it again.
- Look at every image you replace before you commit it.

## What a change includes

The full list is in CONTRIBUTING, under **What a change includes**. A change brings along:

- tests beside the existing ones
- an entry under `## Unreleased` in [CHANGELOG.md](CHANGELOG.md). Never edit a section for a release that has already been published.
- the user guide, when the change alters something a person sees or does
- screenshots, when a page in them changes. Retake them by hand (see **Commands**); the kit's public-domain titles and the posters in `tools/screenshot-posters/` (see its `SOURCES.md`) are the only content allowed in them.
- `openapi.json` and the third-party notices, when they change
- a line in the V1 plan, plus anything left open under **What is still open**
- an ADR or a dated amendment, when the design changes

## Branches, commits and pull requests

- Work for an issue goes on the branch created from that issue (`123-short-title`). Other work goes on `v1/sNN-short-name`. Cut either one from `origin/main` with `--no-track`.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/), usually with a scope: `feat(transport): …`, `fix(nodes): …`, `docs(adr): …`, `docs: …`. The summary says what is now true. The body says, in plain words, why the change was made and what was checked: which tests, which acceptance scripts, how many checks passed.
- Commits are signed. Never bypass signing (`--no-gpg-sign`, `-c commit.gpgsign=false`) and never skip hooks. If signing fails, stop and say so.
- Never amend or force-push a commit that is already on a remote. Fetch before amending.
- Don't push, tag or publish releases unless the maintainer asks. Release steps are in [docs/guides/releases.md](docs/guides/releases.md).
- Each slice is one pull request into `main`. It is merged with a merge commit.

## Writing

Read [Writing in CONTRIBUTING.md](CONTRIBUTING.md#writing) before you write docs, help texts, UI strings, messages or a changelog entry. It's this project's style guide and the source of truth.

Report what was actually run and what it showed. Don't claim that a check passed, or that a file was looked at, unless it was.

## Safety

- Never print, commit or log credentials, API keys or node keys, including test ones.
- Don't put personal data (names, home paths, addresses, hostnames) in code, docs or test fixtures.
- Media for tests is either synthetic (ffmpeg test patterns from `acceptance/Library.ps1`) or public domain or CC0 (`tools/music-fixtures/`). Don't add copyrighted media, posters or samples.
- Use a separate data folder (`-data=`) for development instances. Don't touch an installed Nodearr or its data.
- Report security problems through [SECURITY.md](SECURITY.md), not a public issue.

## Known traps

- Tests run on one machine. A pass doesn't prove that two machines can connect, because a host firewall or NAT may block them. Say so when reporting a networking change.
- Lidarr reads the first bitrate-like number in a release title as its quality, so an album titled "96'" parses as MP3-96. Check a music fixture's title and track list against what Lidarr matches, not only the track count.
- The V5 OpenAPI still lists the TV endpoints inherited from Sonarr. They respond, but nothing in Nodearr uses them.

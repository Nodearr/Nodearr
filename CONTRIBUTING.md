# Contributing

Thank you for wanting to help. This is how work on Nodearr is done.

## Before you start

- **Read the [overview](docs/overview-end-to-end-design.md)** and the decision records ([`docs/adr/`](docs/adr/)) for the part you want to change. They say why things are the way they are, and what was tried and rejected.
- **A change of design starts as a decision.** If it changes how something works rather than fixing it, write it up first: a new ADR, or an amendment to the one it changes, dated, saying what changed and why.
- **Security problems** go through [SECURITY.md](SECURITY.md), not an issue.

## Building

### Prerequisites

- .NET SDK 10.0.300 or newer
- Node.js 20
- Yarn 4
- Rust 1.91 or newer, for the transport sidecar

### The web UI

Written to `_output/UI`:

```sh
yarn install
```

```sh
yarn build
```

### The application

Written to `_output/net10.0`:

```sh
dotnet build src/Nodearr.sln
```

Warnings are errors, StyleCop included, and so are NuGet vulnerability advisories: a newly published advisory against a dependency fails the build until the package is updated.

### The transport sidecar

`nodearr-transport` is a small Rust program in `transport/` that Nodearr starts itself:

```sh
cargo build --release --manifest-path transport/Cargo.toml
```

```sh
cp transport/target/release/nodearr-transport _output/net10.0/
```

Or leave it where it is and point `Nodearr__Transport__BinaryPath` at it. Without the sidecar Nodearr still runs, and System > Status says the peer transport is not running.

## Running

```sh
dotnet run --project src/NzbDrone.Console/Nodearr.Console.csproj -- -nobrowser -data=/path/to/data
```

- `-data=` sets where `config.xml`, `nodearr.db`, the node's key and the logs are kept. Without it Nodearr uses `%ProgramData%\Nodearr` on Windows and `~/.config/Nodearr` elsewhere.
- `-nobrowser` stops it opening a browser on start.
- The UI is at `http://localhost:6633`. The port is changed under Settings > General.

### The API

The API is versioned. `GET /api` reports V5 as current and V3 as deprecated.

```text
GET /ping                     no API key
GET /api/v5/system/status     API key required
```

Send the key as an `X-Api-Key` header or an `apikey` query parameter. It is generated on first run and stored as `ApiKey` in `config.xml`.

The whole API is described in [`src/Nodearr.Api.V5/openapi.json`](src/Nodearr.Api.V5/openapi.json), generated from the code by `pwsh build/Build-OpenApi.ps1`. A debug build also serves it at `/docs/v5/openapi.json`. It still lists the TV endpoints inherited from Sonarr, which answer but which nothing in Nodearr uses.

## Testing

```sh
dotnet test src/Nodearr.sln
```

- CI runs the unit tests on Linux and Windows for every push and pull request, lints and builds the web UI, builds the sidecar, and builds the image ([release guide](docs/guides/releases.md#continuous-integration)).
- The web UI is linted with `yarn lint` and `yarn stylelint`, and type-checked with `tsc`.
- The sidecar is `cargo test --manifest-path transport/Cargo.toml`.
- The acceptance scripts in [`acceptance/`](acceptance/README.md) run a built Nodearr from the outside against real Sonarr and Radarr containers: registration and catalog import, pairing and trust, sharing, searching, transfers, and the Docker image deployed twice. Run the one for the part you changed.
- Something the scripts do not cover, such as a new Arr app, is checked by hand against the real app, and what was run and what it showed goes in the decision record. [Testing by hand](docs/guides/ui-test.md) sets up Nodearr, Sonarr and Radarr with a small library to try things in the pages.

## What a change includes

- **Tests** for what it does, beside the existing ones in `src/NzbDrone.Core.Test` and the other test projects.
- **The user guide** ([`docs/user-guide/`](docs/user-guide/README.md)), when it changes something a person sees or does. When a page in its screenshots changes, take them again with `tools/docs-screenshots.mjs`, which says how.
- **The changelog** ([CHANGELOG.md](CHANGELOG.md)), under Unreleased.
- **The third-party notices**, when it adds, removes or updates a dependency: `pwsh build/Build-Notices.ps1`, and commit the result ([licences](docs/guides/releases.md#licences)).
- **The API description**, when it changes the API: `pwsh build/Build-OpenApi.ps1`, and commit `src/Nodearr.Api.V5/openapi.json` and `src/Nodearr.Api.V3/openapi.json`.
- **The V1 plan** ([`docs/plans/v1-implementation-plan.md`](docs/plans/v1-implementation-plan.md)): a line for the slice, and anything it leaves open under "What is still open".

## Branches and commits

- Branch from `main`, named for the slice: `v1/s48-docs`.
- Commit messages start with a type and a scope, as `feat(sharing): …`, `fix(ui): …` or `docs: …`, then say in plain words what is now true. The body says why, and what was checked.
- Commits are signed.
- One pull request per slice, into `main`.

## Writing

Docs, help texts and messages are written for someone who runs the Arr apps and may never have read a line of Nodearr's code: plain words, short sentences, what to do rather than what went wrong inside. A message about a failure says what failed, what it means, and what to try.

## Licence

Nodearr is GPL-3.0, and most of it is derived from Sonarr (see [NOTICE.md](NOTICE.md)). By contributing you agree that your work is published under the same licence.

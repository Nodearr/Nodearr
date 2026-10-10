# Nodearr

Nodearr is a self-hosted, Servarr-compatible, trusted peer-to-peer media availability and transfer service. It runs next to your existing Sonarr and Radarr, lets a small private group of trusted peers see who has an acceptable copy of something, and retrieves verified files directly from them while the normal Arr import workflow stays in place.

**Status: pre-alpha.** Nodearr can connect to Sonarr and Radarr, register itself in them as an indexer and download client, and import their libraries into a local catalog. Two nodes can pair with an invite, block or revoke each other, choose what to share, and see each other's shared catalogs compared with their own. They do not transfer files yet. The V1 plan is in [docs/plans/v1-implementation-plan.md](docs/plans/v1-implementation-plan.md).

## Prerequisites

- .NET SDK 10.0.300 or newer
- Node.js 20
- Yarn 4

## Build

Frontend, written to `_output/UI`:

```sh
yarn install
yarn build
```

Backend, written to `_output/net10.0`:

```sh
dotnet build src/Nodearr.sln
```

Warnings are errors, including NuGet vulnerability audit findings, so a newly published advisory against a dependency fails the build until the package is updated.

## Peer transport

Peer features need the `nodearr-transport` sidecar, a small Rust program in `transport/`. Nodearr starts it itself. Build it with Rust 1.91 or newer and put the binary next to Nodearr:

```sh
cargo build --release --manifest-path transport/Cargo.toml
```

```sh
cp transport/target/release/nodearr-transport _output/net10.0/
```

Or leave it where it is and point `Nodearr__Transport__BinaryPath` at it. Without the sidecar Nodearr still runs, and System > Status reports that the peer transport is not running.

## Run

The entry point is `src/NzbDrone.Console/Nodearr.Console.csproj`:

```sh
dotnet run --project src/NzbDrone.Console/Nodearr.Console.csproj -- -nobrowser -data=/path/to/data
```

- `-data=` sets where `config.xml`, `nodearr.db` and logs are kept. Without it Nodearr uses `%ProgramData%\Nodearr` on Windows and `~/.config/Nodearr` elsewhere.
- `-nobrowser` stops it opening a browser on start.
- The UI is at `http://localhost:6633`. Change `Port` in `config.xml` to use another port.

## API

The API is versioned. `GET /api` reports V5 as current and V3 as deprecated.

```text
GET /ping                     no API key
GET /api/v5/system/status     API key required
GET /api/v3/system/status     API key required
```

Send the key as an `X-Api-Key` header or an `apikey` query parameter. It is generated on first run and stored as `ApiKey` in `config.xml`.

## Acceptance tests

The scripts in `acceptance/` run a built Nodearr from the outside and check whole slices of behaviour: registration and catalog import against real Sonarr and Radarr containers, and pairing, blocking and revoking between two Nodearr instances. See [acceptance/README.md](acceptance/README.md).

## Documentation

- [End-to-end design overview](docs/overview-end-to-end-design.md)
- [V1 implementation plan](docs/plans/v1-implementation-plan.md)
- [Architecture decision records](docs/adr/)
- [Sequence diagrams](docs/diagrams/)
- [Research notes](docs/research/), including the [inventory of inherited Sonarr features](docs/research/inherited-sonarr-features.md)

## License and origin

Nodearr is licensed under [GPL-3.0](LICENSE). Most of the current backend and frontend is derived from [Sonarr](https://github.com/Sonarr/Sonarr), and the Arr Apps settings feature is adapted from [Prowlarr](https://github.com/Prowlarr/Prowlarr), both GPL-3.0. See [NOTICE.md](NOTICE.md). Nodearr is not affiliated with or endorsed by either project.

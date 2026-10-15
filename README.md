# Nodearr

Nodearr is a self-hosted, Servarr-compatible, trusted peer-to-peer media availability and transfer service. It runs next to your existing Sonarr and Radarr, lets a small private group of trusted peers see who has an acceptable copy of something, and retrieves verified files directly from them while the normal Arr import workflow stays in place.

**Status: pre-alpha.** Nodearr can connect to Sonarr and Radarr, register itself in them as an indexer and download client, and import their libraries into a local catalog. Two nodes can pair with an invite, block or revoke each other, choose what to share, and see each other's shared catalogs compared with their own. Sonarr and Radarr can search Nodearr, grab what a trusted node shares, and import it: the file is fetched directly from the other node and verified first. It builds into one Docker image that sits beside Sonarr and Radarr (see `docker/docker-compose.yml`). All of this has been run between two deployments on one machine. A run between two machines on different networks is the one thing still to do: `docs/guides/two-machine-test.md`. The V1 plan is in [docs/plans/v1-implementation-plan.md](docs/plans/v1-implementation-plan.md).

## Run it with Docker

This is the way to run Nodearr beside Sonarr and Radarr. It needs only Docker.

```sh
docker build -t nodearr .
```

```sh
docker compose -f docker/docker-compose.yml up -d
```

Edit `docker/docker-compose.yml` first: it needs the paths to your libraries, and it explains the two things that have to be right, the shared `/downloads` folder and the networking. The UI is then at `http://localhost:6633`.

- `/config` holds the database and the node's key. Keep it. A node that loses it is a new node that nobody trusts.
- `/downloads` must be the same folder Sonarr and Radarr have at `/downloads`.
- Libraries are mounted read-only, at the same paths Sonarr and Radarr use.

## Release packages

Like Sonarr and Radarr, Nodearr is also packaged for Linux, macOS and Windows, to run without Docker. What each package holds, what a machine needs, and how a release is made are in [docs/guides/releases.md](docs/guides/releases.md).

The rest of this page is for working on Nodearr itself.

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
- The UI is at `http://localhost:6633`. The port is changed under Settings > General.

## API

The API is versioned. `GET /api` reports V5 as current and V3 as deprecated.

```text
GET /ping                     no API key
GET /api/v5/system/status     API key required
GET /api/v3/system/status     API key required
```

Send the key as an `X-Api-Key` header or an `apikey` query parameter. It is generated on first run and stored as `ApiKey` in `config.xml`.

## Acceptance tests

The scripts in `acceptance/` run a built Nodearr from the outside and check whole slices of behaviour against real Sonarr and Radarr containers: registration and catalog import, pairing and trust, sharing, searching, transfers, and the Docker image deployed twice. See [acceptance/README.md](acceptance/README.md).

## Documentation

- [End-to-end design overview](docs/overview-end-to-end-design.md)
- [V1 implementation plan](docs/plans/v1-implementation-plan.md)
- [Architecture decision records](docs/adr/)
- [Sequence diagrams](docs/diagrams/)
- [Research notes](docs/research/), including the [inventory of inherited Sonarr features](docs/research/inherited-sonarr-features.md)

## License and origin

Nodearr is licensed under [GPL-3.0](LICENSE). Most of the current backend and frontend is derived from [Sonarr](https://github.com/Sonarr/Sonarr), and the Arr Apps settings feature is adapted from [Prowlarr](https://github.com/Prowlarr/Prowlarr), both GPL-3.0. See [NOTICE.md](NOTICE.md). Nodearr is not affiliated with or endorsed by either project.

# Nodearr

Nodearr lets a few friends who trust each other get media from one another's libraries, through the Arr apps they already run. It sits beside Sonarr, Radarr, Whisparr and Lidarr as one more indexer and download client. When your Sonarr wants an episode a friend has, it finds it in Nodearr, grabs it, and imports it like any other download. The file comes straight from your friend's machine to yours, checked against its hash before your Arr app sees it.

Nobody else is involved. There is no account, no server of ours that sees your library, and no relay that carries your files. Each of you runs Nodearr on your own machine, decides what the others may see, and can end a friendship at any time.

**Status: pre-release.** Everything V1 set out to do works and has been run between machines on different home networks, with no port forwarded. It has not yet been lived with for weeks. See [what is still open](docs/plans/v1-implementation-plan.md#what-is-still-open).

## What it works with

| App | What Nodearr does with it |
| --- | --- |
| **Sonarr** | Reads its series, episodes and files. Shares whole series, seasons or single episodes. Finds episodes for it by TVDB, TMDb or IMDb ID, by scene numbering, and by air date for daily shows. |
| **Radarr** | Reads its movies and files. Finds movies for it by TMDb or IMDb ID. |
| **Whisparr v3** (Eros) | Reads its films and scenes. Finds films by ID, and scenes by studio, release date, title and code, as Whisparr searches for them. |
| **Whisparr v2** | The same, with each site's scenes. A v2 and a v3 find each other's scenes. |
| **Lidarr** | Reads its artists, albums and tracks. Shares whole artists, offers albums only when they are complete, and moves an album as a folder of tracks. |
| **Prowlarr** | Adds Nodearr to Prowlarr as an indexer, so you can search it there with your others. Prowlarr holds no library, so nothing is read from it. |

Adult content from Whisparr is only ever found by Whisparr's own searches, and albums only by Lidarr's. Sonarr and Radarr never see either.

Readarr is not supported: it was archived in June 2025.

## How it works, in short

1. You add your Arr apps to Nodearr. Nodearr adds itself to each one as an indexer and a download client, and reads its library.
2. You create an invite and send it to a friend. When they paste it into their Nodearr, the two nodes trust each other.
3. You choose what each friend may see. Nothing is shared until you do.
4. Each node fetches what its friends share, and shows what they have that you are missing, or have better.
5. When an Arr app searches, Nodearr answers from what your friends share. When it grabs a result, Nodearr fetches the file directly from the friend who has it, checks it, and hands it to the Arr app to import.

A public relay run by [n0](https://n0.computer) helps two nodes find each other. Files never go through it: if the two machines cannot reach each other directly, the transfer fails and says why. More in the [overview](docs/overview-end-to-end-design.md).

## Getting it

Downloads are on the [Releases](https://github.com/Nodearr/Nodearr/releases) page, as Sonarr's are:

- **Windows:** an installer that starts Nodearr in the tray when you sign in, or as a service. There is also a zip.
- **Linux and macOS:** an archive per platform. Start it with `./Nodearr`.
- **Docker:** `ghcr.io/nodearr/nodearr`, for amd64 and arm64. [`docker/docker-compose.yml`](docker/docker-compose.yml) puts it beside Sonarr and Radarr, and explains the two things that have to be right: the shared downloads folder and the networking.

Then open `http://localhost:6633`. The first page takes you through connecting your apps, naming your node and inviting a friend.

What each package holds and what a machine needs is in [the release guide](docs/guides/releases.md).

## Documentation

- [Overview](docs/overview-end-to-end-design.md): what Nodearr is and how the parts fit together.
- [Releases](docs/guides/releases.md): what a release holds, and how one is made.
- [Testing between two machines](docs/guides/two-machine-test.md), and [on Windows](docs/guides/two-machine-test-windows.md).
- [Decision records](docs/adr/): why each part works the way it does.
- [Sequence diagrams](docs/diagrams/).
- [The V1 plan](docs/plans/v1-implementation-plan.md): what was built, in what order, and what is still open.
- [Research notes](docs/research/), including [what Nodearr inherited from Sonarr](docs/research/inherited-sonarr-features.md).

## Working on Nodearr

### Prerequisites

- .NET SDK 10.0.300 or newer
- Node.js 20
- Yarn 4
- Rust 1.91 or newer, for the transport sidecar

### Build

The web UI, written to `_output/UI`:

```sh
yarn install
```

```sh
yarn build
```

The application, written to `_output/net10.0`:

```sh
dotnet build src/Nodearr.sln
```

Warnings are errors, including NuGet vulnerability audit findings, so a newly published advisory against a dependency fails the build until the package is updated.

The transport sidecar, `nodearr-transport`, a small Rust program in `transport/` that Nodearr starts itself:

```sh
cargo build --release --manifest-path transport/Cargo.toml
```

```sh
cp transport/target/release/nodearr-transport _output/net10.0/
```

Or leave it where it is and point `Nodearr__Transport__BinaryPath` at it. Without the sidecar Nodearr still runs, and System > Status says the peer transport is not running.

### Run

```sh
dotnet run --project src/NzbDrone.Console/Nodearr.Console.csproj -- -nobrowser -data=/path/to/data
```

- `-data=` sets where `config.xml`, `nodearr.db`, the node's key and the logs are kept. Without it Nodearr uses `%ProgramData%\Nodearr` on Windows and `~/.config/Nodearr` elsewhere.
- `-nobrowser` stops it opening a browser on start.
- The UI is at `http://localhost:6633`. The port is changed under Settings > General.

### API

The API is versioned. `GET /api` reports V5 as current and V3 as deprecated.

```text
GET /ping                     no API key
GET /api/v5/system/status     API key required
```

Send the key as an `X-Api-Key` header or an `apikey` query parameter. It is generated on first run and stored as `ApiKey` in `config.xml`.

### Tests

- Unit tests: `dotnet test src/Nodearr.sln`. CI runs them on Linux and Windows ([release guide](docs/guides/releases.md#continuous-integration)).
- Acceptance tests: the scripts in [`acceptance/`](acceptance/README.md) run a built Nodearr from the outside against real Sonarr and Radarr containers: registration and catalog import, pairing and trust, sharing, searching, transfers, and the Docker image deployed twice.

### Releases and licences

A release is made by pushing a tag ([release guide](docs/guides/releases.md#making-a-release)). When a dependency changes, regenerate the third-party notices with `pwsh build/Build-Notices.ps1` and commit them ([licences](docs/guides/releases.md#licences)).

## Licence and origin

Nodearr is licensed under [GPL-3.0](LICENSE). Most of its backend and frontend is derived from [Sonarr](https://github.com/Sonarr/Sonarr), and the Arr Apps settings are adapted from [Prowlarr](https://github.com/Prowlarr/Prowlarr), both GPL-3.0 (see [NOTICE.md](NOTICE.md)). A release also carries other open-source software under its own licences, listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Nodearr is not affiliated with or endorsed by the Sonarr, Radarr, Lidarr, Whisparr, Prowlarr or Servarr projects.

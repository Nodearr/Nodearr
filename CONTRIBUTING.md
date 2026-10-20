# Contributing

Thank you for wanting to help. This is how work on Nodearr is done.

## Before you start

- **Read the [overview](docs/overview-end-to-end-design.md)** and the decision records ([`docs/adr/`](docs/adr/)) for the part you want to change. They say why things are the way they are, and what was tried and rejected.
- **A change of design starts as a decision.** If it changes how something works rather than fixing it, write it up first: a new ADR, or an amendment to the one it changes, dated, saying what changed and why.
- **Security problems** go through [SECURITY.md](SECURITY.md), not an issue.

## Building

### Prerequisites

- [.NET](https://dotnet.microsoft.com/) SDK 10.0.300 or newer
- [Node.js](https://nodejs.org/) 20
- [Yarn](https://yarnpkg.com/) 4
- [Rust](https://www.rust-lang.org/) 1.91 or newer, for the transport sidecar

### The web UI

From the repository root, install dependencies and build the UI. The output is written to `_output/UI`:

```sh
yarn install
```

```sh
yarn build
```

### The application

From the repository root, build the application. The output is written to `_output/net10.0`:

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

On Windows, the binary ends in `.exe`. In PowerShell:

```powershell
Copy-Item transport/target/release/nodearr-transport.exe _output/net10.0/
```

If `CARGO_TARGET_DIR` is set, use the folder Cargo wrote to instead of `transport/target`.

Or leave it where it is and point `Nodearr__Transport__BinaryPath` at it. Without the sidecar Nodearr still runs, and **System > Status** says the peer transport is not running.

## Running

```sh
dotnet run --project src/NzbDrone.Console/Nodearr.Console.csproj -- -nobrowser -data=/path/to/data
```

- `-data=` sets where `config.xml`, `nodearr.db`, the node's key and the logs are kept. Without it Nodearr uses `%ProgramData%\Nodearr` on Windows, `~/.config/Nodearr` on Linux (`$XDG_CONFIG_HOME/Nodearr` when set), and `~/Library/Application Support/Nodearr` on macOS.
- `-nobrowser` stops it opening a browser on start.
- The UI is at `http://localhost:6633`. The port is changed under **Settings > General**.

### The API

The API is versioned. `GET /api` reports V5 as current and V3 as deprecated.

```text
GET /ping                     no API key
GET /api/v5/system/status     API key required
```

Send the key as an `X-Api-Key` header or an `apikey` query parameter. It is generated on first run and stored as `ApiKey` in `config.xml`.

The native V5 API is described in [`src/Nodearr.Api.V5/openapi.json`](src/Nodearr.Api.V5/openapi.json), generated from the code by `pwsh build/Build-OpenApi.ps1`. A debug build also serves it at `/docs/v5/openapi.json`. It still lists the TV endpoints inherited from [Sonarr](https://sonarr.tv/), which answer but which nothing in Nodearr uses. The Newznab and [SABnzbd](https://sabnzbd.org/) compatibility APIs are described in [ADR 0010](docs/adr/0010-arr-contract-newznab-indexer-and-sabnzbd-download-client.md).

## Testing

Build first, then run the unit-test assemblies one at a time, as CI does. They share files and can interfere when run together. This PowerShell 7 command leaves out manual, integration, automation and other-platform tests:

```powershell
$excludedPlatform = if ($IsWindows) { 'LINUX' } else { 'WINDOWS' }
$skip = @('Nodearr.Automation.Test', 'Nodearr.Integration.Test')
$skip += if ($IsWindows) { 'Nodearr.Mono.Test' } else { 'Nodearr.Windows.Test' }
$filter = "TestCategory!=ManualTest&TestCategory!=IntegrationTest&TestCategory!=AutomationTest&TestCategory!=$excludedPlatform"
$failed = @()
foreach ($assembly in Get-ChildItem _tests/net10.0 -Filter 'Nodearr.*.Test.dll') {
    if ($skip -contains $assembly.BaseName) { continue }
    dotnet test $assembly.FullName --filter $filter
    if ($LASTEXITCODE -ne 0) { $failed += $assembly.BaseName }
}
if ($failed) { throw "Tests failed: $($failed -join ', ')" }
```

- CI runs the unit tests on Linux and Windows for every push and pull request, lints and builds the web UI, builds the sidecar, and builds the image ([release guide](docs/guides/releases.md#continuous-integration)).
- The web UI is linted with `yarn lint` and `yarn stylelint`, and type-checked with `yarn typecheck`, as CI does. It checks our own code, not the type files that come with packages.
- The sidecar is built with `cargo test --manifest-path transport/Cargo.toml`. It currently has no Rust unit tests; its behaviour is exercised by the acceptance scripts.
- The acceptance scripts in [`acceptance/`](acceptance/README.md) run a built Nodearr from the outside against real Sonarr and [Radarr](https://radarr.video/) containers: registration and catalog import, pairing and trust, sharing, searching, transfers, and the [Docker](https://docs.docker.com/) image deployed twice. Run the one for the part you changed.
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

Follow the [Google developer documentation style guide](https://developers.google.com/style/highlights), especially its guidance on [procedures](https://developers.google.com/style/procedures), [UI elements](https://developers.google.com/style/ui-elements) and [links](https://developers.google.com/style/cross-references).

- Write each task so a reader can start there. State prerequisites and the app where each action happens. A version-specific section must include the details that version needs; do not make a [Whisparr](https://whisparr.com/) v3 user read the v2 instructions. Repeat short essential context, or link explicitly to a shared procedure.
- Use numbered steps for a sequence. Put a condition before the step it affects, and a warning before the action. Include the final save or confirmation and any result the reader needs to recognize success.
- Use **bold** for exact UI labels, matching their wording and capitalization: go to **System > Status**, or select **Check connection** for a friend on **Nodes > Trusted Nodes**. Format a navigation path as one bold phrase. Use code formatting for commands, paths, configuration keys and literal values.
- Link third-party projects at their first useful mention on a page. Use official project pages for introductions and specific documentation for instructions. Use descriptive link text and avoid repeatedly linking the same destination unless a long page has separate entry points.
- Use the same term for the same thing. Distinguish a default from a requirement, and explain unfamiliar terms where the reader needs them. Read the whole section after editing to catch repetition, missing context and awkward transitions.

UI help strings are plain text unless their component supports markup. Do not insert Markdown into a string that would display the formatting characters to users.

## Licence

Nodearr is GPL-3.0, and most of it is derived from Sonarr (see [NOTICE.md](NOTICE.md)). By contributing you agree that your work is published under the same licence.

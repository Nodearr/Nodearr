# Contributing

Thank you for wanting to help. This is how work on Nodearr is done.

## Before you start

- **Read the [overview](docs/overview-end-to-end-design.md)** and the decision records ([`docs/adr/`](docs/adr/)) for the part you want to change. They say why things are the way they are, and what was tried and rejected.
- **A change of design starts as a decision.** If it changes how something works rather than fixing it, write it up first: a new ADR, or an amendment to the one it changes, dated, saying what changed and why.
- **Security problems** go through [SECURITY.md](SECURITY.md), not an issue.

## Building and testing

[README: Working on Nodearr](README.md#working-on-nodearr) has the prerequisites and the commands. In short:

```sh
yarn install
```

```sh
yarn build
```

```sh
dotnet build src/Nodearr.sln
```

```sh
dotnet test src/Nodearr.sln
```

- Warnings are errors, StyleCop included, and so are NuGet vulnerability advisories.
- The web UI is linted with `yarn lint` and `yarn stylelint`, and type-checked with `tsc`.
- The sidecar is `cargo test --manifest-path transport/Cargo.toml`.
- The acceptance scripts in [`acceptance/`](acceptance/README.md) check whole features against real Sonarr and Radarr containers. Run the one for the part you changed.
- Something the scripts do not cover, such as a new Arr app, is checked by hand against the real app, and what was run and what it showed goes in the decision record.

## What a change includes

- **Tests** for what it does, beside the existing ones in `src/NzbDrone.Core.Test` and the other test projects.
- **The user guide** ([`docs/user-guide/`](docs/user-guide/README.md)), when it changes something a person sees or does.
- **The changelog** ([CHANGELOG.md](CHANGELOG.md)), under Unreleased.
- **The third-party notices**, when it adds, removes or updates a dependency: `pwsh build/Build-Notices.ps1`, and commit the result ([releases](docs/guides/releases.md#licences)).
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

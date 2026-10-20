# Notice: reused code and attribution

Nodearr is free software licensed under the GNU General Public License, version 3 (see [LICENSE](LICENSE)). It is a modified work that contains code from other GPL-3.0 projects. This file records where that code came from and that it has been changed, as GPL-3.0 section 5 requires.

Nodearr is not affiliated with, sponsored by or endorsed by the [Sonarr](https://sonarr.tv/), [Prowlarr](https://prowlarr.com/) or [Servarr](https://wiki.servarr.com/) teams.

## Sonarr

- Source: <https://github.com/Sonarr/Sonarr>
- License: GPL-3.0
- Upstream copyright notice: `Copyright 2014-<year> sonarr.tv (GNU General Public v3)`
- Version taken: the `v5-develop` branch as of May 2026. The reference checkout used for the port was at commit `dfb157382b20a2d4eb5f5828a6c1e276c0d6b160` (2026-05-16), and the inherited database migrations stop at 230, the same as that commit.

What is derived from Sonarr:

- Nearly everything under `src/`: the `NzbDrone.*` projects, and `Nodearr.Api.V3`, `Nodearr.Api.V5` and `Nodearr.Http`, which are Sonarr's `Sonarr.Api.V3`, `Sonarr.Api.V5` and `Sonarr.Http` renamed.
- The `frontend/` application, its build configuration, and the root `package.json`, lint and [TypeScript](https://www.typescriptlang.org/) configuration.
- The test projects and their fixtures.

## Prowlarr

- Source: <https://github.com/Prowlarr/Prowlarr>
- License: GPL-3.0

What is derived from Prowlarr:

- The application sync feature under `src/NzbDrone.Core/Applications/` and `src/Nodearr.Api.V5/Applications/`, and its settings UI under `frontend/src/Settings/Applications/`.

## Changes made by Nodearr

Nodearr's changes begin on 2026-05-21. The full record is the git history of this repository. In summary:

- "Sonarr" was renamed to "Nodearr" throughout the inherited code: namespaces, assembly and product names, user-facing text and URLs. As a result, identifiers and hostnames in inherited code such as `services.nodearr.com`, `skyhook.nodearr.com` and `sentry.nodearr.com` are renamed Sonarr endpoints. They are not services operated by Nodearr.
- The frontend navigation and routes were replaced with Nodearr's Catalog, Nodes, Activity, Settings and System pages.
- Prowlarr's application sync was ported in and reshaped to register Nodearr's indexer, download client and webhook in library apps, import their catalogs, and register an indexer in Prowlarr.
- Cover image resizing, and with it the SixLabors.ImageSharp dependency, was removed.

[docs/research/inherited-sonarr-features.md](docs/research/inherited-sonarr-features.md) lists the inherited Sonarr features and what Nodearr intends to do with each.

## Third-party software

A release also carries work by others under their own licences: the [Rust](https://www.rust-lang.org/) crates in the transport sidecar, the NuGet packages in the application, the npm packages in the web UI, and the [.NET](https://dotnet.microsoft.com/) runtime. They are listed, with each licence's text, in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and, for the .NET runtime, in `dotnet-LICENSE.txt` and `dotnet-THIRD-PARTY-NOTICES.txt` in each self-contained package.

## Assembly copyright

The rename had changed the assembly copyright string in `src/Directory.Build.props` from `sonarr.tv` to `nodearr.com`, so built assemblies no longer named Sonarr. Since 2026-10-15 it reads `Copyright 2014-<year> sonarr.tv, and Nodearr's contributors (GNU General Public v3)`.

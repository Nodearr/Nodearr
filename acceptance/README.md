# Acceptance tests

Scripts that run a built Nodearr against real Arr applications in containers and check a whole slice of behaviour from the outside, through the HTTP APIs.

They need PowerShell 7, Docker and ffmpeg, and a built Nodearr:

```sh
dotnet build src/Nodearr.sln
```

Each script starts from nothing, takes a few minutes, prints one line per check and exits non-zero if any check fails. Everything it creates goes in `.run/`, which git ignores, and in its own Docker Compose project.

## S3: registration and catalog import

```sh
pwsh acceptance/Run-S3CatalogImport.ps1
```

Checks, against pinned Sonarr and Radarr containers:

- Adding an application registers one Nodearr download client and one pinned Nodearr indexer in the Arr app, even while Nodearr's catalog is empty.
- Files the Arr apps import show up in the local catalog with their series or movie identity, quality and size, including one file that holds two episodes.
- Path mappings make the Arr apps' paths readable by Nodearr.
- A file deleted in Sonarr is marked missing on the next refresh and removed by a purge.
- Syncing again creates no duplicates, the scheduled jobs are the right ones, and deleting an application removes its catalog.

Nodearr runs on the host on port 6633. The containers come from `spikes/arr-contract/docker-compose.yml` with `docker-compose.library.yml` layered on top, which puts the Arr libraries on the host so Nodearr can read them. They publish on 127.0.0.1 only, on ports 18989 and 17878.

The Arr apps get their files by "downloading" them through the S1 stub transfer, which copies synthetic ffmpeg test patterns. The real indexer and download client arrive in S6 and S7.

Options:

- `-KeepRunning` leaves Nodearr and the containers up, so you can open `http://localhost:6633/catalog/local`.
- `-Down` stops everything and deletes `.run/`.

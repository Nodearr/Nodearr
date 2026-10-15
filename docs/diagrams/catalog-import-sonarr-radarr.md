# Catalog import from Sonarr and Radarr

Adding an Arr app to Nodearr does two things. Nodearr registers itself inside the app as a download client and a pinned indexer, and it imports the app's library into the local catalog. Sonarr and Radarr stay the source of truth for media identity, library membership, paths, quality and profiles.

This is the flow as built in S3 and checked by `acceptance/Run-S3CatalogImport.ps1`.

## Adding an application

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Nodearr
    participant Arr as Sonarr or Radarr

    User->>Nodearr: Add application: Arr URL, API key, Nodearr URL as the app sees it
    Nodearr->>Arr: GET /api/v3/system/status
    Arr-->>Nodearr: appName and version
    Nodearr->>Arr: GET /api/v3/downloadclient
    Nodearr->>Arr: POST /api/v3/downloadclient (SABnzbd, name Nodearr)
    Arr->>Nodearr: Connection test: /sabnzbd/api version and get_config
    Arr-->>Nodearr: Download client ID
    Nodearr->>Arr: POST /api/v3/indexer (Newznab, name Nodearr, downloadClientId set, RSS off)
    Arr->>Nodearr: Connection test: /newznab/api caps, then a search with no IDs
    Nodearr-->>Arr: Peer candidates, or a stand-in item when there are none
    Arr-->>Nodearr: Indexer saved
    Nodearr->>Nodearr: Queue a catalog refresh for this application
```

A sync every 6 hours repeats the two lookups and only writes when something differs.

## Refreshing the catalog

```mermaid
sequenceDiagram
    autonumber
    participant Nodearr
    participant Arr as Sonarr or Radarr
    participant Store as Catalog tables

    Note over Nodearr: Every 6 hours, on demand, and after an application is saved
    Nodearr->>Arr: GET /api/v3/qualityprofile
    alt Sonarr
        Nodearr->>Arr: GET /api/v3/series
        loop Each series
            Nodearr->>Arr: GET /api/v3/episode?seriesId=N
            Nodearr->>Arr: GET /api/v3/episodefile?seriesId=N
        end
    else Radarr
        Nodearr->>Arr: GET /api/v3/movie (each movie carries its file)
    end
    Nodearr->>Store: Insert what is new, update what changed
    Nodearr->>Store: Mark what the app no longer reports as missing
    Nodearr->>Store: Remove items missing for more than 7 days
```

If the app cannot be read, nothing is marked missing and the error is recorded against the application.

A file that holds several episodes is one row in `CatalogEpisodeFiles`. Each of its episodes in `CatalogEpisodes` points at it.

## Reading the files

The catalog stores each file's path as the Arr app reports it. When Nodearr needs the file itself, it applies the path mappings, which are keyed by the Arr app's host:

```text
Sonarr reports   /tv/Pingu/Season 1/Pingu.S01E01.mkv
Path mapping     host 127.0.0.1, /tv/  ->  D:\Media\TV\
Nodearr reads    D:\Media\TV\Pingu\Season 1\Pingu.S01E01.mkv
```

The Local Catalog page shows whether each file is readable at its mapped path.

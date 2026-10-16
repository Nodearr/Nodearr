# Catalog import from the Arr apps

Adding an Arr app to Nodearr does two things. Nodearr registers itself inside the app as a download client, a pinned indexer and a webhook, and it imports the app's library into the local catalog. The Arr apps stay the source of truth for media identity, library membership, paths, quality and profiles ([ADR 0006](../adr/0006-arr-apps-as-catalog-source-of-truth.md)).

Built in S3 for Sonarr and Radarr and checked by `acceptance/Run-S3CatalogImport.ps1`. Whisparr and Lidarr are read the same way, through their own APIs, below.

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
    Nodearr->>Arr: POST /api/v3/notification (Webhook, name Nodearr, to /api/v5/arr/webhook/ID)
    Arr->>Nodearr: Test event
    Nodearr->>Nodearr: Queue a catalog refresh for this application
```

A sync every 6 hours repeats the lookups and only writes when something differs. With Full Sync it puts back anything changed by hand.

## Refreshing the catalog

```mermaid
sequenceDiagram
    autonumber
    participant Nodearr
    participant Arr as Sonarr or Radarr
    participant Store as Catalog tables

    Note over Nodearr: Every 12 hours, on demand, and after an application is saved
    Nodearr->>Arr: GET /api/v3/qualityprofile
    alt Sonarr
        Nodearr->>Arr: GET /api/v3/series
        loop Each series
            Nodearr->>Arr: GET /api/v3/episode?seriesId=N
            Nodearr->>Arr: GET /api/v3/episodefile?seriesId=N
        end
    else Radarr, Whisparr v3
        Nodearr->>Arr: GET /api/v3/movie (each movie carries its file)
    else Whisparr v2
        Nodearr->>Arr: GET /api/v3/series (sites)
        loop Each site
            Nodearr->>Arr: GET /api/v3/episode?seriesId=N&includeEpisodeFile=true (scenes)
        end
    else Lidarr
        Nodearr->>Arr: GET /api/v1/artist, then GET /api/v1/album
        loop Each artist
            Nodearr->>Arr: GET /api/v1/trackfile?artistId=N
        end
    end
    Nodearr->>Store: Insert what is new, update what changed
    Nodearr->>Store: Mark what the app no longer reports as missing
    Nodearr->>Store: Remove items missing for more than 7 days
```

If the app cannot be read, nothing is marked missing and the error is recorded against the application.

A file that holds several episodes is one row in `CatalogEpisodeFiles`. Each of its episodes in `CatalogEpisodes` points at it.

## When the app changes something

The webhook names the series, movie or artist an event was about, and only that title is read again, within seconds. Whisparr v2 names a site, not a scene, so its whole library is read again.

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Sonarr
    participant Nodearr
    participant Store as Catalog tables

    Arr->>Nodearr: POST /api/v5/arr/webhook/ID: Download, series 10
    Nodearr-->>Arr: 200
    Nodearr->>Arr: GET /api/v3/series/10, its episodes and episode files
    Nodearr->>Store: Merge that series only
    Nodearr->>Nodearr: Mark friends' files against it again, and tell friends if something was taken back
```

## Reading the files

The catalog stores each file's path as the Arr app reports it. When Nodearr needs the file itself, it applies the path mappings, which are keyed by the Arr app's host:

```text
Sonarr reports   /tv/Pingu/Season 1/Pingu.S01E01.mkv
Path mapping     host 127.0.0.1, /tv/  ->  D:\Media\TV\
Nodearr reads    D:\Media\TV\Pingu\Season 1\Pingu.S01E01.mkv
```

The Local Catalog page shows whether each file is readable at its mapped path.

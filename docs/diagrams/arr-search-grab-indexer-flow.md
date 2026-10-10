# Arr search, grab and import flow

Nodearr is a Newznab indexer and a SABnzbd-compatible download client to the Arr apps. A search returns peer candidates. A grab returns a small NZB that is only a ticket naming one candidate. The Arr app hands that ticket to Nodearr's download client surface, which is where the transfer job is created.

The messages below are the ones recorded in the S1 spike against Sonarr 4.0.20, Radarr 6.4.4 and Prowlarr 2.6.5, with the API key removed. See [ADR 0010](../adr/0010-arr-contract-newznab-indexer-and-sabnzbd-download-client.md).

Since S6 the search half is real: results come from what trusted nodes share, matched by ID. See [ADR 0015](../adr/0015-the-indexer-id-matching-release-titles-and-candidate-ids.md) and the diagram before the last one here. The download half is still the S1 stub until S7.

## Direct: the Arr app has Nodearr as its indexer

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Sonarr or Radarr
    participant Newznab as Nodearr /newznab/api
    participant Sab as Nodearr /sabnzbd/api
    participant Jobs as Nodearr transfer jobs
    participant Files as Shared /downloads

    Note over Arr,Sab: Registration, when the indexer and download client are saved
    Arr->>Sab: mode=version, then mode=get_config
    Sab-->>Arr: version, complete_dir, categories tv and movies
    Arr->>Newznab: t=caps
    Newznab-->>Arr: search modes, supported IDs, categories
    Arr->>Newznab: t=tvsearch&cat=5030,5040 (no IDs)
    Newznab-->>Arr: at least one item, or the indexer is rejected

    Note over Arr,Newznab: Search
    Arr->>Newznab: Sonarr: t=tvsearch&tvdbid=75669&imdbid=tt0366069&season=1&ep=1
    Arr->>Newznab: Radarr: t=movie&tmdbid=10331&imdbid=0063350
    Newznab-->>Arr: RSS items: title, guid, pubDate, size, enclosure link
    Arr->>Arr: Parse title, apply quality profile and custom formats

    Note over Arr,Jobs: Grab
    Arr->>Newznab: t=get&id=CANDIDATE
    Newznab-->>Arr: NZB ticket naming the candidate
    Arr->>Sab: POST mode=addfile&cat=tv (multipart, TITLE.nzb)
    Sab->>Jobs: Read candidate ID from the NZB, create job
    Sab-->>Arr: nzo_ids: [SABnzbd_nzo_...]

    Note over Arr,Files: Tracking and import
    loop Every refresh
        Arr->>Sab: mode=queue&category=tv
        Sab-->>Arr: slot: nzo_id, status Downloading, mb, mbleft
        Arr->>Sab: mode=history&category=tv
    end
    Jobs->>Files: Verified file moved to complete/CATEGORY/TITLE/
    Sab-->>Arr: history slot: status Completed, storage path
    Arr->>Files: Import into the library
    Arr->>Sab: mode=history&name=delete&del_files=1&archive=1
```

## Through Prowlarr

Prowlarr relays the search. The Arr app still fetches the ticket from Nodearr itself, because Prowlarr answers the download link with a redirect by default, and it still uses Nodearr as its download client.

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Sonarr or Radarr
    participant Prowlarr
    participant Newznab as Nodearr /newznab/api
    participant Sab as Nodearr /sabnzbd/api

    Note over Prowlarr,Newznab: Registration in Prowlarr
    Prowlarr->>Newznab: t=caps
    Prowlarr->>Newznab: t=search&extended=1 (no IDs, no categories)
    Prowlarr->>Arr: Create indexer "Nodearr (Prowlarr)" at http://prowlarr:9696/1/

    Note over Arr,Newznab: Search
    Arr->>Prowlarr: t=tvsearch with tvdbid, season, ep
    Prowlarr->>Newznab: t=tvsearch&cat=5030,5040,5045,5000&tvdbid=75669&imdbid=0366069&season=1&ep=2
    Newznab-->>Prowlarr: RSS items
    Prowlarr-->>Arr: Same items, guid unchanged, link pointing at Prowlarr

    Note over Arr,Sab: Grab
    Arr->>Prowlarr: Download link
    Prowlarr-->>Arr: Redirect to Nodearr's t=get link
    Arr->>Newznab: t=get&id=CANDIDATE
    Newznab-->>Arr: NZB ticket
    Arr->>Sab: POST mode=addfile, then queue and history as above
```

## Where a search result comes from

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Ryan's Sonarr
    participant Newznab as Ryan's Nodearr /newznab/api
    participant Catalog as Ryan's copy of John's offer
    participant John as John's Nodearr

    Note over Catalog,John: Earlier, and again whenever John's sharing changes
    John-->>Catalog: The files John shares with Ryan: IDs, quality, size, release name if any
    Catalog->>Catalog: Give each file a candidate ID and a release title

    Arr->>Newznab: t=tvsearch&tvdbid=75669&imdbid=tt0366069&season=1&ep=2
    Newznab->>Catalog: TV files where any ID agrees, season 1, holding episode 2
    Catalog-->>Newznab: One file from John
    Newznab-->>Arr: title Pingu.S01E02.720p.WEB-DL.AAC.1.0.x264, guid nodearr:CANDIDATE, size, pubDate
    Arr->>Arr: Parse the title: Pingu, S01E02, WEBDL-720p. Wanted? Then grab.
    Arr->>Newznab: t=get&id=CANDIDATE
    Newznab->>Catalog: Is this still offered by a trusted node?
    Newznab-->>Arr: NZB ticket naming CANDIDATE, or "No such item"
```

Nothing in the answer names John or John's file. The candidate ID is a keyed hash only Ryan's Nodearr can produce or look up.

## Failed transfer

```mermaid
sequenceDiagram
    autonumber
    participant Arr as Sonarr or Radarr
    participant Newznab as Nodearr /newznab/api
    participant Sab as Nodearr /sabnzbd/api

    Arr->>Sab: mode=history&category=tv
    Sab-->>Arr: slot: status Failed, fail_message "Nodearr transfer failed: NoDirectRoute ..."
    Arr->>Arr: Record downloadFailed, add release to blocklist
    Arr->>Sab: mode=history&name=delete&del_files=1&archive=0
    Arr->>Newznab: Search again for the same episode or movie
    Newznab-->>Arr: The same candidate
    Arr->>Arr: Reject it: "Release is blocklisted"
```

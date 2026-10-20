# ADR 0010: The Arr Contract Is a Newznab Indexer Plus a SABnzbd-Compatible Download Client

## Status

Accepted. Proven by the S1 spike on 2026-10-10.

## Context

ADR 0005 decided that Nodearr is both an indexer and a download client, but left the download-client surface open. The V1 plan chose to emulate [SABnzbd](https://sabnzbd.org/), with the grab returning a small NZB that carries a Nodearr candidate ID, and made proving that choice a go/no-go gate.

The spike had to show a real import, not just accepted API calls: search, grab, NZB accepted, queue, history Completed, and the file landing in the Arr library. It had to work directly and through [Prowlarr](https://prowlarr.com/), and a failed transfer had to end up blocklisted.

## Decision

Nodearr presents itself to Arr apps as two things on its own HTTP port, both authenticated with Nodearr's API key:

1. A Newznab indexer at `/newznab/api`, with `t=caps`, `t=search`, `t=tvsearch`, `t=movie` and `t=get`.
2. A SABnzbd-compatible API at `/sabnzbd/api`, with `mode=version`, `get_config`, `fullstatus`, `addfile`, `queue`, `history`, `queue&name=delete`, `history&name=delete` and `retry`.

A grab works like this:

- `t=get` returns an NZB that is only a ticket. It names one Nodearr candidate in a `<meta type="x-nodearr-candidate">` element and has one `<file>` with one `<segment>`, which is the least Arr's NZB validation accepts.
- The Arr app uploads those same bytes to `mode=addfile`. Nodearr reads the candidate ID back out, creates the transfer job and answers with an `nzo_id`.
- That `nzo_id` is the download ID that ties the Arr app's history, queue and import together.

Qualities and custom formats stay with the Arr apps, which parse the release title Nodearr returns.

## Evidence

`spikes/arr-contract/Run-ArrContractSpike.ps1` starts a fresh Nodearr and fresh containers, configures everything through the Arr APIs, and checks the results. The stub lives in `src/Nodearr.Http/ArrContractSpike/`. A "transfer" in the stub is a throttled local file copy.

Versions: [Sonarr](https://sonarr.tv/) 4.0.20.3014, [Radarr](https://radarr.video/) 6.4.4.10685, Prowlarr 2.6.5.5623 (linuxserver images pinned by digest in `spikes/arr-contract/docker-compose.yml`).

All nine scenarios passed:

| Scenario | Result |
| --- | --- |
| Sonarr accepts Nodearr as a SABnzbd client and a Newznab indexer, with its connection tests running on save | Pass |
| Radarr accepts the same | Pass |
| Sonarr, direct: automatic search, grab, transfer, import to `/tv/Pingu/Season 1/` | Pass |
| Radarr, direct: automatic search, grab, transfer, import to `/movies/Night of the Living Dead (1968)/` | Pass |
| Sonarr failure: transfer fails, Sonarr records it, blocklists the release and does not grab it again | Pass |
| Radarr failure: the same | Pass |
| Prowlarr accepts Nodearr as a Generic Newznab indexer and syncs it into Sonarr and Radarr | Pass |
| Sonarr through Prowlarr: interactive search, manual grab, transfer, import | Pass |
| Radarr through Prowlarr: the same | Pass |

In every import the Arr history shows `grabbed` then `downloadFolderImported`, the download ID is the `nzo_id` Nodearr issued, and the file exists in the library volume at its full size.

The media was synthetic: ffmpeg test patterns named after public-domain titles.

## What the Arr apps actually send

These are the requests recorded by the stub, with the API key removed.

Registration, when the indexer and download client are saved:

```text
GET /sabnzbd/api?mode=version&output=json
GET /sabnzbd/api?mode=get_config&output=json
GET /newznab/api?t=caps
GET /newznab/api?t=tvsearch&cat=5030,5040&extended=1&offset=0&limit=100
```

Search:

```text
Sonarr    GET /newznab/api?t=tvsearch&cat=5030,5040&extended=1&offset=0&limit=100&tvdbid=75669&imdbid=tt0366069&season=1&ep=1
Radarr    GET /newznab/api?t=movie&cat=2030,2040&extended=1&offset=0&limit=100&tmdbid=10331&imdbid=0063350
Prowlarr  GET /newznab/api?t=tvsearch&extended=1&cat=5030,5040,5045,5000&tvdbid=75669&imdbid=0366069&season=1&ep=2&limit=100&offset=0
```

Grab and tracking:

```text
GET  /newznab/api?t=get&id=tv-e1
POST /sabnzbd/api?mode=addfile&cat=tv&priority=-100&output=json      (multipart, file field "name", <title>.nzb)
GET  /sabnzbd/api?mode=queue&start=0&limit=0&category=tv&output=json
GET  /sabnzbd/api?mode=history&start=0&limit=60&category=tv&output=json
GET  /sabnzbd/api?mode=history&name=delete&del_files=1&value=<nzo_id>&archive=1&output=json    (after import)
GET  /sabnzbd/api?mode=history&name=delete&del_files=1&value=<nzo_id>&archive=0&output=json    (after failure)
```

`fullstatus`, `retry` and `queue&name=delete` are implemented in the stub but no scenario made an Arr app call them. Arr calls `fullstatus` only when `complete_dir` is relative, `retry` on a manual retry, and the queue delete when a user removes a running download.

## Findings that constrain later slices

These came out of the spike and were not known beforehand.

1. **An indexer with nothing to offer cannot be registered.** On save, Sonarr sends a search with categories but no IDs and rejects the indexer with HTTP 400 if it returns no items: "Query successful, but no results in the configured categories were returned from your indexer." `forceSave=true` does not bypass it. A new Nodearr with no trusted peers, or with nothing shared to it yet, hits this. S3's auto-registration needs an answer, and S6 has to decide what an ID-less query returns.
2. **Arr sends every ID it has, in different formats.** Sonarr sends `tvdbid` with `imdbid=tt0366069`. Radarr and Prowlarr send `imdbid` without the `tt`. A candidate must match when any ID it carries agrees. The first version of the stub required all of them to match and returned nothing, and Sonarr then fell back to a title search (`q=Pingu`).
3. **Advertising `q` invites title searches.** With `q` in the capabilities, Sonarr retries by title, including alternate titles, whenever the ID search is empty. The plan calls for ID-based matching, so S6 should decide whether to advertise `q` for `tv-search` and `movie-search` at all. Prowlarr's own test and category sync use plain `t=search`, so basic search still has to answer.
4. **Usenet blocklisting matches on release title and publish date, not on GUID.** A blocklisted release stayed rejected ("Release is blocklisted") on the next search. Sonarr's `ReleaseComparer.SameNzb` treats a release as the blocklisted one when the title matches and the publish date is identical, or when it comes from another indexer with the same date and size. Nodearr must therefore keep `pubDate` stable for a candidate, or a candidate that keeps failing would be grabbed again on every search. It also means a second peer offering the same title with the same publish date counts as the same release, so the date should be derived from the file, not from the peer.
5. **Through Prowlarr, the Arr app still fetches the NZB from Nodearr itself.** Prowlarr's indexer has "Redirect" on by default, so `t=get` arrived from Sonarr and Radarr, not from Prowlarr. The link Nodearr puts in search results must be reachable from the Arr apps, not only from Prowlarr.
6. **Prowlarr-synced indexers are not pinned and have RSS on.** Prowlarr created `Nodearr (Prowlarr)` with `downloadClientId=0` and `enableRss=true`. With no pin, the Arr app chooses among its Usenet download clients itself. That worked here because Nodearr was the only one. A user who also runs a real SABnzbd could have a Nodearr ticket sent to it, where it would fail. With RSS on, the Arr app also polls Nodearr with an ID-less query on its RSS interval.
7. **The direct indexer can be pinned.** `downloadClientId` on Sonarr's and Radarr's indexer resource was accepted and kept, so the plan's "one indexer pinned to one download client" works for direct registration.
8. **Sizes and runtimes are checked for real.** Sonarr's default minimum for WEBDL-720p is 3 MB per minute of episode runtime, applied to the size the indexer reports. Both apps probe the downloaded file and refuse to import one they class as a sample: under 90 seconds for a 5 minute episode, under 10 minutes for anything over 30 minutes. The spike's media was sized to clear both, and the size Nodearr reports must be the real file size.
9. **Failure text reaches the user.** The `fail_message` Nodearr returned ("NoDirectRoute...", "VerificationFailed...") appears verbatim in the Arr app's history and blocklist. S7's typed failures should be written for that audience.
10. **Download-client categories matter.** Arr filters the queue and history by its configured category (`tv`, `movies`), and `get_config` must list that category or the client fails its test.
11. **The reported path must be one the Arr app can see.** Nodearr ran on the host and reported `/downloads/complete/<category>/<title>`, the path as mounted in the containers. S3's path mappings and S8's shared `/downloads` volume are what make this true in a real deployment.

## Consequences

### Positive

- No changes to Sonarr, Radarr or Prowlarr are needed, and no seeding or ratio rules apply, which was the reason for choosing SABnzbd over qBittorrent emulation.
- Failure handling, blocklisting and re-search come from the Arr apps for free.
- The NZB ticket keeps peer addresses and transfer details out of anything an Arr app or Prowlarr stores.

### Negative / trade-offs

- Nodearr is labelled "SABnzbd" and "usenet" in the Arr UI, which may confuse users.
- Nodearr reports a SABnzbd version (`4.5.1` in the stub) and inherits whatever Arr assumes about that version.
- An NZB ticket is only meaningful to the Nodearr that issued it. Sent to any other Usenet client, it fails.
- The API key appears in the `t=get` link, as it does for every Newznab indexer, so it is visible in Arr and Prowlarr logs and history.

## Follow-up work

- ~~S3: decide how auto-registration copes with an empty catalog (finding 1), and pin the indexer to the download client.~~ Done: S3, see the amendments.
- ~~S6: ID matching semantics (finding 2), whether to advertise `q` (finding 3), stable `pubDate` and size (finding 4), signed opaque GUIDs, and what an ID-less query returns.~~ Done: ADR 0015.
- ~~S6: guidance for Prowlarr users on pinning the synced indexer and on RSS (finding 6).~~ Done: Nodearr now manages its indexer in Prowlarr and switches Prowlarr's copies off (ADR 0007, amendments).
- ~~S7: idempotent `addfile`, real job persistence, and failure messages written for the Arr history (finding 9).~~ Done: ADR 0016.
- ~~Replace the stub in `src/Nodearr.Http/ArrContractSpike/` with the real implementation. It is not production code: jobs are in memory and candidates come from a JSON file.~~ Done: S6 and S7. The stub is gone.

## Amendments

- 2026-10-10, finding 1 resolved in S3 (owner's suggestion): when a query has no IDs and no text and would return nothing, Nodearr answers with one stand-in item per requested category family, titled "Nodearr connection test, not a release". The title matches no series or movie, so the Arr apps ignore it outside the connection test, and its link cannot be grabbed. With this, Sonarr 4.0.20 and Radarr 6.4.4 both saved Nodearr's indexer while the catalog was completely empty (`acceptance/Run-S3CatalogImport.ps1`). S6 still decides what an ID-less query returns once there are real candidates.
- 2026-10-10, S6 replaced the indexer half of the stub with the real one, and answered the questions left for it (ADR 0015). Findings 2 and 3: a file matches when any ID agrees, and the TV and movie searches do not advertise `q`. Finding 4: the publish date is when the peer's Arr app imported the file. Finding 1: a query with no IDs lists what is on offer, and falls back to the stand-in when there is nothing. Finding 6: what a Prowlarr user has to set by hand is written down in ADR 0015. The download-client half of the stub stays until S7. `spikes/arr-contract/Run-ArrContractSpike.ps1` needs both halves, so it runs only on the `v1/s1-s2-spikes` branch.
- 2026-10-10, S7 replaced the download-client half of the stub with the real one (ADR 0016). Nothing of the stub is left. Finding 9: failures have names and a sentence written for the Arr app's history. Finding 11: Nodearr reports paths as it sees them, and an Arr app that sees the download folder elsewhere is told with a remote path mapping.
- 2026-10-10, finding 7 implemented in S3: adding an application in Nodearr creates the download client and the indexer in the Arr app and sets the indexer's `downloadClientId` to that client.
- 2026-10-11: **deleting an application takes Nodearr back out of the Arr app.** Until now the indexer and the download client that Nodearr had added stayed behind, both pointing at a Nodearr that no longer answered for that app. Deleting an application now deletes them first: the indexer, then the download client it is pinned to. They are found the way they are kept in step, by kind and by the name "Nodearr". It is tried once and waited on for 15 seconds. If the Arr app cannot be reached the application is deleted all the same, a warning is logged, and the two entries are left for the owner to delete. The question before deleting says so. Where two applications are set up for the one Arr app, deleting one leaves the registration for the other.
- 2026-10-14: **a webhook connection as well.** The owner's direction: Nodearr hears of new, upgraded and deleted files from Sonarr and Radarr through the Arr apps' own webhook, which Nodearr adds itself. Beside its indexer and download client, Nodearr now keeps a third entry in each Arr app it registers in: a Webhook connection named "Nodearr", as a user would add one under **Settings > Connect**.
  - It posts to `/api/v5/arr/webhook/{application}` on the Nodearr address the application was set up with, and carries Nodearr's API key in an `X-Api-Key` header, which both Sonarr 4 and Radarr 6 have.
  - Sonarr's events are On File Import, On File Upgrade, On Rename, On Series Add, On Series Delete, On Episode File Delete and On Episode File Delete For Upgrade. Radarr's are their movie equivalents. Every other event is named and turned off, Import Complete included, since it repeats what each file's import says.
  - It is kept in step, and taken out on deleting the application, the same way as the other two: by kind and by name. It goes out first.
  - The Arr apps test a connection when it is saved, by sending a Test event. Nodearr answers it and does nothing else.

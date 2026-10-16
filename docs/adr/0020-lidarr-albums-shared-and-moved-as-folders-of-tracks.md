# ADR 0020: Lidarr Albums Are Shared and Moved as Folders of Tracks

## Status

Accepted. Slice L1 (Lidarr's library in the catalog) built and checked on 2026-10-15. Slices L2 to L5 follow.

## Context

Lidarr is still developed and is the music app of the Arr family. Readarr, the books app, was archived in June 2025 and is not supported.

Everything Nodearr shares so far is one file: an episode file, a movie file, a Whisparr scene. Lidarr does not work that way:

- Its library is artists, their albums, and the albums' tracks. Each track is a file of its own.
- It grabs and imports a whole album at once, as one download holding a folder of tracks. A release with one track missing is a different release, not most of one.
- It knows an artist by its MusicBrainz ID and an album by its MusicBrainz release group, which every edition of the album shares.
- It searches an indexer by text, `t=music&artist=...&album=...`, not by ID. Its categories are 3000 (Audio), 3010 (MP3) and 3040 (Lossless).
- Its API is `/api/v1`. Its download client field is `musicCategory`, and its webhook names the artist (`"artist": { "id": ... }`).

## Decision

Lidarr is supported the way its releases work: the unit Nodearr offers, matches, transfers and hands back is an album, and an album is a folder of track files.

### L1: the library

Three tables beside the series and movie ones: `CatalogArtists`, `CatalogAlbums` and `CatalogTrackFiles`. A track file has the same columns as the other file tables, and its artist and album.

- A full read takes the quality profiles, `/artist`, `/album`, then `/trackfile?artistId=` for each artist, as Lidarr lists track files only per artist or album.
- A webhook names an artist, and that artist is read again: the artist, `/album?artistId=` and its track files. Nothing else of the library is touched.
- Lidarr sends only a track's full path. The part below the artist's folder is kept as its relative path.
- Registration is Sonarr's and Radarr's: the SABnzbd client on `musicCategory`, a Newznab indexer on 3000, 3010 and 3040 pinned to it, and the webhook on import, upgrade, rename, artist added, artist deleted and album deleted.

### L2: sharing and offers by album

An offer lists albums, by release group and artist ID, each with its tracks' names, sizes and qualities. An album is offered only when Lidarr holds every one of its track files. A peer's album is compared with the local library by release group: missing, an upgrade, or held.

### L3: a transfer of several files

A transfer job gets parts. The parts of an album arrive one after another into `incomplete/<download id>/`, each verified as a single file is now (ADR 0016). The folder moves into `complete` only when every part is verified, so Lidarr never sees half an album. A part that fails fails the album. Resuming works per part.

### L4: the indexer

`t=music` and `t=search` with an audio category are answered by text, artist and album words, as Whisparr's scenes are (ADR 0015). Release titles are Lidarr's own scene name where it kept one, or `Artist - Album (Year) [Quality]`.

### L5: a live test

Two Lidarr instances, as for every other app: an album missing on one is found, grabbed, moved and imported, with identical tracks at both ends.

## Consequences

### Positive

- A music library is shared with the same rules, trust and checks as the rest.
- The catalog's refresh and merge machinery takes the three tables unchanged.
- Multi-file transfers are a general addition. A season pack could use them later.

### Negative / trade-offs

- A Lidarr library with many artists costs one request per artist on every full read.
- An album is all or nothing. A peer with one track short offers nothing for that album.
- Lidarr finds releases by text, so a match is only as good as the artist and album names on both sides.

# ADR 0020: Lidarr Albums Are Shared and Moved as Folders of Tracks

## Status

Accepted. Slices L1 (Lidarr's library in the catalog) and L2 (sharing and comparing albums) built and checked on 2026-10-15. Slices L3 to L5 follow.

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

- A share list holds whole artists, as it holds whole movies. An artist takes in albums that arrive later. Albums are not put on a list one by one.
- An offer lists albums, `a-{application}-{album}`. Each says its artist, title, year, release day, kind of release, both MusicBrainz IDs, and its tracks in Lidarr's order. A track says its extension, size and quality, and nothing of its name or path, as a file does (ADR 0014). Its ID, `t-{application}-{track file}`, is what a transfer asks for. The album's quality is the one most of its tracks have, and its size is all of theirs.
- An album is offered only when Lidarr holds every track of it: the album's statistics say as many track files as tracks, and the catalog holds that many.
- A node says it knows albums when it asks for a catalog (`CatalogAcceptsMusic`). An older node does not, and is sent none, as it would take an album for a movie.
- A peer's album is compared with the local Lidarr by release group: not in the library, missing (no tracks, or only some), an upgrade by the artist's quality profile, or held.
- An album whose tracks will not do, or a kind of file this node does not know, is kept as it was sent, so the catalog still adds up to the peer's version, but is never matched, searched for or fetched.
- Sonarr and Radarr are never shown an album, whatever they ask.

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

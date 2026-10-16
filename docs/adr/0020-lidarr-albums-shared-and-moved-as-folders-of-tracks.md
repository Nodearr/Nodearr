# ADR 0020: Lidarr Albums Are Shared and Moved as Folders of Tracks

## Status

Accepted. All five slices built and checked on 2026-10-15: L1 (Lidarr's library in the catalog), L2 (sharing and comparing albums), L3 (moving an album), L4 (the indexer) and L5 (the live test).

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

- A share list holds whole artists, as it holds whole movies. An artist takes in albums that arrive later. ~~Albums are not put on a list one by one.~~ They are now, see the amendment of 2026-10-16.
- An offer lists albums, `a-{application}-{album}`. Each says its artist, title, year, release day, kind of release, both MusicBrainz IDs, and its tracks in Lidarr's order. A track says its extension, size and quality, and nothing of its name or path, as a file does (ADR 0014). Its ID, `t-{application}-{track file}`, is what a transfer asks for. The album's quality is the one most of its tracks have, and its size is all of theirs.
- An album is offered only when Lidarr holds every track of it: the album's statistics say as many track files as tracks, and the catalog holds that many.
- A node says it knows albums when it asks for a catalog (`CatalogAcceptsMusic`). An older node does not, and is sent none, as it would take an album for a movie.
- A peer's album is compared with the local Lidarr by release group: not in the library, missing (no tracks, or only some), an upgrade by the artist's quality profile, or held.
- An album whose tracks will not do, or a kind of file this node does not know, is kept as it was sent, so the catalog still adds up to the peer's version, but is never matched, searched for or fetched.
- Sonarr and Radarr are never shown an album, whatever they ask.

### L3: a transfer of several files

- A transfer of an album has parts, its tracks in the order offered. The album's size and progress count all of them.
- The parts arrive one after another into `incomplete/<download id>/`, named as Lidarr names its own files, such as `Slint - Spiderland - 01 - Breadcrumb Trail.flac` (amendment of 2026-10-16), each verified as a single file is (ADR 0016) and each with its own three attempts. Lidarr identifies the tracks by their tags. A Lidarr that does not rename tracks, which is its default, keeps these names and puts every album's tracks straight into the artist's folder, so each album's names have to be its own. Bare `01.flac` names were tried first and would have collided.
- The folder moves into `complete` only when the last part is verified, so Lidarr never sees half an album.
- A part that fails for good fails the album, and nothing of it is kept. A part that does not match its hash starts over, and the parts before it are kept.
- Each part is its own transfer in the transport, `<download id>-<n>`, so nothing of one can be taken for another. The sidecar needs no change: a file's name is opaque to it.
- An album stays with the node it came from. It does not move to another node that offers one of its tracks.
- The sending node checks each part as it checks any file: the track is offered only while its album is whole and its artist shared.

### L4: the indexer

- The caps offer `audio-search` with `q,artist,album`, which is what Lidarr needs to search by artist and album, and the Audio categories 3000, 3010 (MP3) and 3040 (Lossless).
- `t=music`, and `t=search` in an audio category, are answered by text: every word of the artist, the album and any query has to be in an album's artist, title, year or release title. Lidarr reads the title to judge the rest, as Whisparr does for a scene (ADR 0015).
- Albums are found only when an audio category is asked for, never by a search that names none, so Sonarr and Radarr never see them. Lidarr's browse (its RSS sync and connection test) lists only albums, with a stand-in when there are none yet.
- An album is filed under 3040 when its quality is lossless (FLAC, ALAC, APE, WavPack, WAV), and 3010 otherwise.
- Its release title is the name Lidarr kept for the release where there is one, or `Artist - Album (Year) [Quality]` as Lidarr names folders, with Lidarr's MP3-320 written MP3 320.

### L5: a live test

Two Lidarr 3.1.0.4875 instances (checked against the hash Lidarr's update service publishes) and two fresh Nodearr nodes on one machine, with Slint added to both. A held *Spiderland*, the six-track CD release, as six silent FLACs tagged with its MusicBrainz IDs and each track's length. B held none of it.

- Registration: Lidarr took the indexer (3000, 3010, 3040, pinned to Nodearr's download client), the download client on `music`, and the webhook.
- B's Nodearr showed A's album as missing, six tracks, FLAC.
- Lidarr B's interactive search listed `Slint - Spiderland (1991) [FLAC]` from Nodearr, approved.
- Its automatic search grabbed it. The six tracks arrived and were verified in about 8 seconds, and Lidarr imported all six at its next check of finished downloads, about a minute later.
- Each imported track was byte for byte the one A holds, in order.
- Lidarr's webhook had B's Nodearr read the artist again, and A's album then showed as held.

The first run named the parts `01.flac` and so on, and Lidarr, which does not rename by default, put them straight into the artist's folder under those names. The parts are now named by the release as well (L3), and a second run from fresh data imported them as `Slint - Spiderland (1991) [FLAC] - 01.flac` and so on.

## Follow-up work

- ~~Sending each track's title, as an episode's title is sent, would let a Lidarr that does not rename keep names such as `01 - Breadcrumb Trail.flac`. Lidarr's track titles are not in the catalog yet.~~ Done, see the amendment of 2026-10-16.

## Amendments

- 2026-10-16: **tracks are named as Lidarr names its own.** A track arrived as `Slint - Spiderland (1991) [FLAC] - 01.flac`: unique, but nothing of which song it is. Now:
  - Nodearr reads each artist's tracks from Lidarr (`/track?artistId=`, one more request per artist) and keeps each track file's title, its number on its disc, and the disc.
  - An offered track says them, as an episode's title and number are said. Like those, they are what Lidarr knows the track as, not the file's name. A track Lidarr has not matched says nothing more than before. The album's tracks are in Lidarr's order, by disc and number, where every file is matched.
  - A friend's title is kept only when it is at most 300 characters with nothing a file name cannot hold. Otherwise the title alone is dropped and the album kept.
  - A track arrives as Lidarr's own default names it, `Artist - Album - 01 - Title`, and `Artist - Album - 1-01 - Title` for an album on several discs. Two tracks of an album named alike get ` (2)` and so on, and a name is cut at 150 characters for Windows' path limit. A track with no title keeps the name it had. A Lidarr that does not rename puts these straight into the artist's folder, and the artist and album in each name keep them apart.
  - Run again between two Lidarr 3.1.0.4875 instances from fresh data: Lidarr B imported the six tracks of *Spiderland* as `Slint - Spiderland - 01 - Breadcrumb Trail.flac` to `Slint - Spiderland - 06 - Good Morning, Captain.flac`, straight into the artist's folder, each byte for byte the one A holds. Lidarr handed the same ticket over twice, four seconds apart, and was given the same download back both times, so the album was fetched once (ADR 0010, finding 9).

## Consequences

### Positive

- A music library is shared with the same rules, trust and checks as the rest.
- The catalog's refresh and merge machinery takes the three tables unchanged.
- Multi-file transfers are a general addition. A season pack could use them later.

### Negative / trade-offs

- A Lidarr library with many artists costs one request per artist on every full read.
- An album is all or nothing. A peer with one track short offers nothing for that album.
- Lidarr finds releases by text, so a match is only as good as the artist and album names on both sides.
- 2026-10-16: **albums are shared one by one, as seasons are.** A share list held only whole artists. Lidarr monitors by album, as Sonarr does by season and episode, so a list now holds an artist whole or some of its albums, and the Hidden list can hold one album of an artist that is otherwise shared. An album is covered by its artist or itself. A whole artist still takes in albums that arrive later, and an album on a list under its whole artist on the same list is dropped as saying nothing more. An album is still offered only when Lidarr holds every track, and Catalog > Sharing says which are not yet whole. In the API an album is `albumArrId` on a share key, and `GET /api/v5/sharing/titles/{application}/{artist}/albums` lists an artist's albums. Checked between two nodes: hiding one album took it off the friend's side within a second, and unhiding it brought it back at their next sync.


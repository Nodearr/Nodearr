# ADR 0022: Posters Come from Friends' Arr Apps

## Status

Accepted. Part 1 built on 2026-10-16 (S57): a friend's catalog with posters. Part 2, the other pages, is still open in the [V1 plan](../plans/v1-implementation-plan.md#what-is-still-open). S58 was used for the UI test kit.

## Context

A friend's catalog was a list of file names. That is hard to browse: the owner's direction was to add enriched thumbnails "so that browsing is more intuitive", and to cache them.

Every Arr app already holds each title's pictures. Sonarr, Radarr, Lidarr and Whisparr download them from TheTVDB, TMDb, fanart.tv, MusicBrainz and StashDB when a title is added, keep them on disk with smaller copies, and serve them through their own API. Two ways to show them on a friend's node:

- The friend's node fetches each picture from those services itself. That is a call out to services the owner never pointed Nodearr at, which ADR 0018 rules out, and it tells each service what is being browsed.
- The node that offers a title passes on the picture its own Arr app already has, over the same connection the catalog comes by.

## Decision

A poster comes from the friend that offers the title, and nothing else is contacted.

- **The offering node** asks its own Arr app, which ADR 0018 allows: Sonarr's series poster, Radarr's movie poster, Lidarr's album cover (its artist's picture where there is none), Whisparr v3's poster or scene screenshot, and for Whisparr v2 the site's poster. The 500-pixel copy where the app keeps one. Only a JPEG or a PNG of up to 2 MB is passed on.
- **Asked over the control channel**, with a new `poster` message naming an item of the asker's offer. The answer is the image, or none. A friend is answered for a title exactly when it is offered a file of it, by the same rule as files (ADR 0014), and "not offered" and "no poster" are the same answer, so asking teaches nothing. An older Nodearr refuses the message, and is treated as having no posters.
- **Cached on both sides**, on disk under `Posters` in the data folder:
  - the offering node keeps what its Arr app gave it for 7 days, and "no poster" for a day;
  - the browsing node keeps what friends sent for 30 days, "no poster" for a day, and an unreachable friend for 15 minutes before asking again;
  - the browsing node keeps a poster by what the title is, not by which file or friend: a series by TVDB ID, a movie by TMDb or IMDb ID, adult content by Whisparr's IDs, an album by its MusicBrainz release group. A series of a hundred episode files, or a film two friends both have, is fetched once.
- **At most four** posters are asked of friends at a time, and one title once however many of its files are on a page.
- **The browser** gets them from `GET /api/v5/catalog/posters/peer/{id}`, signed in to as the UI's pages are, since an `<img>` cannot send the API key. It may keep each for a day.
- **Catalog > Peer Catalog** shows a poster beside each file, and a **Posters** view: a grid with one tile per title, from however many friends, with its files' statuses. It is paged by title, from `GET /api/v5/catalog/peers/titles`.

## Evidence

- Unit tests for the cache (expiry, "none", names that are not plain), the image check, Whisparr's image paths, the exchange (offered, not offered, one answer for both), the browsing side (one request per title across files and friends, "none" remembered, unreachable friends, untrusted ones never asked, the keys), and the sharing rule's answer for episode files, movie files and albums.
- Live on 2026-10-16 between two nodes on one machine, each with its own Arr apps:
  - A's Radarr gave Night of the Living Dead's 500-pixel poster, 65 KB, and A's Whisparr v3 a scene's screenshot, 353 KB, which it keeps at one size only. Asked again, B answered from its own copy in under 10 ms.
  - A's Sonarr gave Pingu's poster, fetched once for all three of its episode files, and A's Lidarr gave Spiderland's cover.
  - The Peer Catalog page showed the thumbnails, and the grid one tile for Pingu's three files and one square tile for the album, at desktop and phone width.

## Consequences

### Positive

- A friend's catalog can be browsed by picture, with nothing contacted that the owner did not point Nodearr at.
- The pictures are the ones the friend's own Arr app shows, so they match what the friend sees.
- A poster costs one request per title, ever, until it is a month old.

### Negative / trade-offs

- A title's picture is only as good as the friend's Arr app's. A title it has no picture for has none here either.
- Whisparr v3 keeps a scene's screenshot at one size, so those are larger to send.
- The browsing node's disk holds a copy of every poster it has shown. At about 70 KB each, 10,000 titles are under a gigabyte.

## Follow-up work

- Posters on the other pages: the cards on Home, the Send form, the Sharing page and Catalog > Local.

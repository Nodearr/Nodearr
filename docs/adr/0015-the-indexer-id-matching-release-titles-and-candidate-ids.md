# ADR 0015: The Indexer: Matching by ID, Release Titles and Candidate IDs

## Status

Accepted. Built and checked in S6 on 2026-10-10.

## Context

ADR 0010 proved that Sonarr and Radarr accept Nodearr as a Newznab indexer, using a stub that read its results from a file. ADR 0014 gave each node a copy of what its trusted peers offer. S6 joins the two: a search from an Arr app is answered from the peer catalog.

ADR 0010 left five questions for this slice: how IDs match, whether to offer text search, what keeps the publish date and size stable, what the opaque ID of a result is, and what a query with no IDs returns. Plan decision 2 had already settled who judges quality: the Arr apps do, from the release title, so the title has to be right.

## Decision

### Matching

Media is matched by ID and nothing else.

- A TV search takes `tvdbid`, `imdbid` and `tmdbid`, with `season` and `ep`. A movie search takes `imdbid` and `tmdbid`.
- The Arr apps send every ID they know in one request. A file matches when any ID it carries agrees. IMDb IDs match with or without the `tt`, because Sonarr sends `tt0366069` and Radarr sends `0063350`.
- TV and movies are kept apart, because TMDb numbers them separately and the same number can be one of each.
- `ep` matches a file that holds that episode, including a file that holds several. A season search returns every file in the season.
- A query with an ID Nodearr does not hold, such as a TVRage ID alone, returns nothing.

The TV and movie searches do not advertise `q`. With `q` advertised, Sonarr falls back to searching by title whenever the ID search is empty, and guessing by title is what ID matching is there to avoid. Plain `t=search` does take `q`, as a match on every word, for people searching by hand in Prowlarr. Radarr uses it as a second try when its ID search is empty, which costs one cheap query.

Categories mean only TV or movies. Any 5xxx category asks for TV and any 2xxx for movies, whichever subcategory it is, so a 2160p file is still found by a Sonarr that asks for SD and HD only. Each result carries the parent category and a subcategory for its resolution.

Results are everything that matches, from every trusted peer. Nodearr does not filter by quality or by what the local library already has. The Arr app does that with its own profiles.

### Release titles

The title is the only thing the Arr apps read to decide quality, custom formats and which episode a result is.

1. If the peer's Arr app kept the name the file was released under, that name is the title. It carries everything the release said about itself.
2. Otherwise Nodearr builds a title from what the peer's Arr app knows about the file: the series or movie title, the year for a movie, season and episodes, edition, any language other than English, quality, dynamic range, audio and video codecs, and the release group.

For example, a file Sonarr holds as `Pingu - S01E02 - Pingu Helps to Deliver the Mail WEBDL-720p.mkv` is offered as `Pingu.S01E02.720p.WEB-DL.AAC.1.0.x264`.

The title is built on the receiving node, from structured fields. The offer no longer carries a file name at all (this amends ADR 0014). A peer's release name is used only if it could be a release name: no path separators or control characters, and Sonarr's parser can read a TV name or a movie name contains its year. Otherwise the built title is used.

The quality words are chosen so that an Arr app reads them back as the same quality. `Bluray-1080p Remux` becomes `1080p.BluRay.Remux`, for example.

### Candidate IDs

Each file a peer offers gets a candidate ID: the first 16 bytes of HMAC-SHA256, keyed with a secret this node creates once and keeps in its database, over the peer's node ID and the peer's own ID for the file.

- **Stable.** The same file from the same peer always gets the same ID, however often the catalog is fetched again. The Arr apps use it as the GUID.
- **Opaque.** It says nothing about the peer or the file. Arr and Prowlarr logs and histories learn neither.
- **Not forgeable.** Without the secret nobody can produce an ID that resolves to anything.

It is a lookup key, not a signed payload. That is deliberate. A signed payload would still verify after the peer stopped offering the file, so it would need a lookup anyway. Here an ID resolves only while a trusted peer currently offers that file, so hiding a title or revoking a peer voids every link already handed out.

`t=get` returns the NZB ticket of ADR 0010, naming the candidate ID. It answers "No such item" for anything else, including the stand-in.

### Publish date and size

The publish date is when the peer's Arr app imported the file. It never changes for a file, which the Arr apps' blocklist depends on (ADR 0010, finding 4). It also differs between two peers' copies of the same release, so when one peer's copy fails and is blocklisted, the other peer's copy can still be grabbed. If a peer sends no date, one is derived from the candidate ID. The size is the real file size.

### A query with no IDs

It lists what is on offer, newest first. This is what the Arr apps' connection test and RSS sync send. When there is nothing to list, the stand-in item from ADR 0010's amendment is returned instead, so a new node can still be registered.

RSS is off on the indexer Nodearr registers. A user can turn it on in the Arr app, and the Arr app will then grab what peers add without being asked.

### Anime

Nodearr registers its Sonarr indexer with an anime category and with "anime standard format search" on. Sonarr then also searches anime by ID, season and episode, which is the one way Nodearr matches.

## Evidence

- 74 unit tests. Every Sonarr quality is built into a title and read back with Sonarr's own parser, which this code base carries. Search runs against a real SQLite database.
- `acceptance/Run-S6Indexer.ps1`: 14 of 14 checks pass. Node A has a library. Node B's own Sonarr 4.0.20 and Radarr 6.4.4 know the same series and movie and have no files. After pairing, the script searches from B's Sonarr and Radarr and reads what they make of the answers:
  - Sonarr finds the episode by ID under its release name, reads it as WEBDL-720p and approves it.
  - A file with no release name gets a built title, and Sonarr reads the right episode and quality from it and approves it.
  - A season search finds both episodes. Radarr finds the movie and approves it.
  - The GUID and publish date are the same after B fetched A's catalog again twice.
  - No answer contains A's node ID, A's file IDs, a path or a file name.
  - A forged ID and a wrong key are refused.
  - Hiding the series on A empties Sonarr's search and voids the old link. Revoking trust empties Radarr's.

## Consequences

### Positive

- The Arr apps decide what to grab with the profiles and custom formats the user already has. Nodearr adds no second set of rules.
- Nothing an Arr app or Prowlarr stores identifies a peer.
- Unsharing reaches the Arr apps as fast as it reaches the node.

### Negative / trade-offs

- **Daily shows are not found.** Sonarr asks for them by air date, and peers are not told air dates.
- **Series with scene numbering can be misnumbered** when the file has no release name, because the built title uses TVDB numbering and Sonarr reads indexer titles as scene numbering for such series.
- **A built title cannot say PROPER or REPACK**, and says only what the peer's Arr app recorded. Custom formats that look for other words in a release name will not match it.
- **Radarr's quality names are not unit tested.** Radarr's parser is not in this code base. The common ones are covered by the acceptance run.
- **Regenerating the secret changes every GUID.** It lives in the database, so a restored backup keeps them.
- **The API key is in the `t=get` link**, as for every Newznab indexer.
- Listing and text search read more rows than they return. That is fine for a few peers and will want care for very large catalogs.

## Using Nodearr through Prowlarr

It works (ADR 0010), with two things a user must do by hand in the Arr app, because Prowlarr creates the indexer there and Nodearr cannot change it:

- **Set the synced indexer's download client to Nodearr.** A Nodearr ticket sent to a real SABnzbd fails.
- **Decide about RSS.** Prowlarr turns it on.

The link in a result uses the address the request came in on. Through Prowlarr that is the address Prowlarr uses for Nodearr, and the Arr app fetches the ticket from it directly, so the Arr app must be able to reach that address too.

Adding the Arr app to Nodearr directly avoids all three.

## Follow-up work

- S7: the download client reads the candidate ID from the ticket and starts a transfer. Until then a grab is refused with a plain reason.
- S7: the sending node checks every transfer request against its sharing rule (ADR 0014).
- Later: air dates in the offer for daily shows, scene numbers for series that use them, and revision (PROPER, REPACK).

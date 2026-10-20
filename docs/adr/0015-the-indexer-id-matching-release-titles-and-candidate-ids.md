# ADR 0015: The Indexer: Matching by ID, Release Titles and Candidate IDs

## Status

Accepted. Built and checked in S6 on 2026-10-10.

## Context

ADR 0010 proved that [Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/) accept Nodearr as a Newznab indexer, using a stub that read its results from a file. ADR 0014 gave each node a copy of what its trusted peers offer. S6 joins the two: a search from an Arr app is answered from the peer catalog.

ADR 0010 left five questions for this slice: how IDs match, whether to offer text search, what keeps the publish date and size stable, what the opaque ID of a result is, and what a query with no IDs returns. Plan decision 2 had already settled who judges quality: the Arr apps do, from the release title, so the title has to be right.

## Decision

### Matching

Media is matched by ID and nothing else.

- A TV search takes `tvdbid`, `imdbid` and `tmdbid`, with `season` and `ep`. A movie search takes `imdbid` and `tmdbid`.
- The Arr apps send every ID they know in one request. A file matches when any ID it carries agrees. IMDb IDs match with or without the `tt`, because Sonarr sends `tt0366069` and Radarr sends `0063350`.
- TV and movies are kept apart, because TMDb numbers them separately and the same number can be one of each.
- `ep` matches a file that holds that episode, including a file that holds several. A season search returns every file in the season.
- A query with an ID Nodearr does not hold, such as a TVRage ID alone, returns nothing.

The TV and movie searches do not advertise `q`. With `q` advertised, Sonarr falls back to searching by title whenever the ID search is empty, and guessing by title is what ID matching is there to avoid. Plain `t=search` does take `q`, as a match on every word, for people searching by hand in [Prowlarr](https://prowlarr.com/). Radarr uses it as a second try when its ID search is empty, which costs one cheap query.

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
- **Opaque.** It says nothing about the peer or the file. Arr and [Prowlarr](https://prowlarr.com/) logs and histories learn neither.
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

- ~~**Daily shows are not found.** Sonarr asks for them by air date, and peers are not told air dates.~~ They are now, see the amendments.
- ~~**Series with scene numbering can be misnumbered** when the file has no release name, because the built title uses TVDB numbering and Sonarr reads indexer titles as scene numbering for such series.~~ Fixed, see the amendments.
- **A built title cannot say PROPER or REPACK**, and says only what the peer's Arr app recorded. Custom formats that look for other words in a release name will not match it.
- **Radarr's quality names are not unit tested.** Radarr's parser is not in this code base. The common ones are covered by the acceptance run.
- **Regenerating the secret changes every GUID.** It lives in the database, so a restored backup keeps them.
- **The API key is in the `t=get` link**, as for every Newznab indexer.
- Listing and text search read more rows than they return. That is fine for a few peers and will want care for very large catalogs.

## Using Nodearr through Prowlarr

It works (ADR 0010), with two things a user must do by hand in the Arr app, because Prowlarr creates the indexer there and Nodearr cannot change it:

- **Set the synced indexer's download client to Nodearr.** A Nodearr ticket sent to a real [SABnzbd](https://sabnzbd.org/) fails.
- **Decide about RSS.** Prowlarr turns it on.

The link in a result uses the address the request came in on. Through Prowlarr that is the address Prowlarr uses for Nodearr, and the Arr app fetches the ticket from it directly, so the Arr app must be able to reach that address too.

Adding the Arr app to Nodearr directly avoids all three.

Amendment, 2026-10-15: adding Prowlarr to Nodearr in **Settings > Apps** does this safely. Nodearr adds itself to Prowlarr with a sync profile that switches off the copies Prowlarr makes in Sonarr and Radarr, so nothing unpinned can grab a Nodearr release (ADR 0007, amendments). The steps above are for someone who adds Nodearr to Prowlarr by hand instead.

## Follow-up work

- ~~S7: the download client reads the candidate ID from the ticket and starts a transfer. Until then a grab is refused with a plain reason.~~ Done: ADR 0016.
- ~~S7: the sending node checks every transfer request against its sharing rule (ADR 0014).~~ Done: ADR 0016.
- Later: ~~air dates in the offer for daily shows,~~ ~~scene numbers for series that use them,~~ and revision (PROPER, REPACK).

## Amendments

- 2026-10-11: **daily shows are found.** Sonarr asks for an episode in one of two ways. For most series it sends a season and an episode number. For a series its owner has marked as daily it sends the year as the season and the month and day as the episode, and it turns down any result whose title is not written with that day. Which way it asks depends on how the series is set up in the Sonarr that is asking. The node that has the file may have it set up the other way, and Nodearr on either side cannot see the other's setting.

  So:

  - **The day each episode aired travels with the file.** A node reads it from its Sonarr and puts it in the offer, beside the episode numbers (ADR 0014). The receiving node keeps only values that are a real date, since the day ends up in a release title and a folder name.
  - **A query by day is matched by day.** A year with a month and day finds the file that holds the episode aired on that day. A year alone finds everything aired in that year. A series whose seasons really are numbered by year is still matched by number first.
  - **The title is written the way it was asked for.** Found by number, a file has the title it always had. Found by day, it is titled with the day, as in `Pingu.1990.03.07.720p.WEB-DL`. The file's own release name is used only when it is written the same way as the question, and with the same day. One consequence for searches by number: a release name written by day is no longer passed through, because Sonarr would turn it down as the wrong season.
  - **The same title lasts from search to import.** The link in a result found by day carries the day, the ticket carries it, and the transfer is named with it. The GUID does not change with the title, so a file is one release to the Arr app however it was asked for.

  Checked with a real Sonarr 4.0.20: the receiving side's Sonarr is switched to daily for a series the sending side's Sonarr calls standard, searches, is answered with a title by day, grabs, and imports the file (`acceptance/Run-S7Transfers.ps1`).

  What this does not cover: a node running an older Nodearr sends no days, so its files are found by number only. After upgrading, a node's days are filled in at its next catalog refresh. Two episodes of one series that aired on the same day cannot be told apart by Sonarr itself. Scene numbering is still open.

- 2026-10-13: **scene numbering.** For some series the scene numbers episodes differently from TVDB, and Sonarr keeps both: its own episodes are TVDB's, and each has a scene season and episode where XEM gives one. American Dad! is one: TVDB's 3x01 is the scene's 2x01. Read from Sonarr's own source and checked against a real Sonarr 4.0.20:
  - Sonarr searches for such an episode by the scene's numbers, and also by TVDB's.
  - Sonarr reads the numbers in an indexer's title as the scene's first. A title saying `S03E01` was read as TVDB's 4x01, a different episode. A title saying `S02E01` was read as 3x01, the right one.

  So a built title with TVDB's numbers named the wrong episode. A search turned it down, and with RSS on Sonarr could have grabbed it as that other episode.

  What changed:
  - Nodearr reads each episode's scene season and episode from Sonarr when it imports the library.
  - An offered file carries the scene's season and episode numbers, only where they differ from TVDB's. An offer for a library with none is exactly what it was, so its version does not change. A file whose episodes the scene puts in two seasons gets none, since one title cannot say both.
  - The receiving node keeps them only when they fit the file: a sensible season, and one number for each episode.
  - A built title uses the scene's numbers when there are any. So do the season and episode attributes in the feed. A release name is still used as it is, since it was named by the scene already.
  - A search by number matches either numbering. Sonarr sends both, and a file found by the numbering Sonarr did not mean is turned down when Sonarr reads its title.

  Checked by unit tests, and by hand against real Sonarrs: node A's Sonarr held American Dad! 3x01 under a plain name and 3x02 under a release name, and node B's Sonarr searched for each. Both were found and mapped to the right episode. The only rejection was the test file's size.

  A node's files get scene numbers at its next library refresh after upgrading. A node still on an older Nodearr sends none, and its files are titled by TVDB's numbers as before.

- 2026-10-14: **the same file from several nodes is one result.** Files carry their [BLAKE3](https://github.com/BLAKE3-team/BLAKE3) hash now (ADR 0014, amendments), so a node can tell when trusted peers hold the very same bytes. They are one release to the Arr app, as one release is one release however many indexers list it.
  - In a search, copies with the same hash from trusted nodes are shown once. The copy shown is the one with the lowest candidate ID, so it is the same one, with the same GUID, as long as the same nodes offer it.
  - In the list of the newest, which is paged in the database, copies that fall on the same page are shown once.
  - A file whose hash its node has not worked out yet stands alone, as before.
  - A grab of the shown copy can carry on from any of the others (ADR 0016, amendments). So if that file fails in the end, every copy has failed, and the Arr app is right to blocklist it.
  - The Peer Catalog page and its API show each file's hash, so the owner can see which nodes hold identical copies.
  - Checked by unit tests and by `acceptance/Run-IdenticalCopies.ps1`: with the same episode on two nodes, B's Sonarr was shown one result and approved it.

- 2026-10-15: **Whisparr v3, and scenes found by text.** [Whisparr](https://whisparr.com/) v3 (Eros) is built on Radarr, and Nodearr keeps its films and scenes as movies marked as adult content. Media is matched by ID wherever the Arr app sends one, and Whisparr does for a film. For a scene it has no ID to send: read from its ReleaseSearchService and Newznab request generator, it searches by text, several ways, depending on how it is set up. The title; the title and the release date; the studio and the title; the studio and the date; the studio and the scene's code. The studio may be its name, an alias, or its name run together (CouchCastingX for Couch Casting-X). The date may be yy.MM.dd or dd.MM.yyyy. The title loses a leading "The", has "&" as "and", and loses apostrophes and accents.
  - A query for adult content is matched by `SceneQuery`: a day in the query, in any of those forms, has to be the scene's release date, and every other word has to be in its studio, written either way, its title, its code or its release title. Peers send what that needs, the item type, studio, release date and code, with Whisparr's ForeignId, StashDB and ThePornDB IDs, and only for Whisparr's files, so every other file's fingerprint is as it was.
  - Adult content is found only by a search in Newznab's XXX family (6000), which is what Whisparr asks in, and is filed there. A search for movies or TV, or one that names no category, never finds it, so Radarr never sees it. Whisparr never sees Radarr's movies either, even where a film and a movie share a TMDb ID.
  - A scene keeps its own release name where the peer's Whisparr kept one. Otherwise it is named as scenes are released: the studio run together, the date as yy.MM.dd, the title, XXX, then the quality.
  - Run against Whisparr 3.6.5 (Eros), two of them, each with its own Nodearr: Nodearr added itself to Whisparr and passed its indexer test, a scene on one side showed as missing on the other, matched by Whisparr's ID, the other side's Whisparr found it with its own text search and approved it, and an automatic search grabbed it, Nodearr fetched it, and Whisparr imported it, the same bytes at both ends.
- 2026-10-15: **Whisparr v2.** Built on Sonarr: a site is a series and a scene is an episode with a release date and no episode number. Nodearr keeps each scene as Whisparr v3 keeps one, a movie marked as a scene, with its site as the studio, so everything above applies to it unchanged. Its library is read one site at a time, each with its scenes and their files in one request. Its webhook names a site, not a scene, so it is read again whole. It searches by text too: the site and the date, the site and the title, the title, or the scene's external ID, which is the scene code above. Whisparr v2 and v3 call themselves the same, so each is told apart by its version, and a v3 added as v2 is told to be added as v3.
  - v2 and v3 know a scene by different IDs. They agree on its studio and its release date, so where no ID links a peer's scene to the local library, those do, the studio written either way.
  - Run against Whisparr 2.2.0.404, two of them: Nodearr read a site's 6,833 scenes on each, and a scene grabbed through it was imported with the same bytes. Then a v2 searched a v3's node: it found the v3's scene with its own text search, approved it, grabbed and imported it, and its comparison marked the scene as had, by studio and date.

- 2026-10-19: **categories select the content family.** The earlier wording that only Whisparr or [Lidarr](https://lidarr.audio/) can find their content describes their normal registration. The Newznab endpoint uses the categories in a request, not the caller's application identity. An API-key holder, including a manual Prowlarr search, can request XXX or Audio categories. A request without them does not find adult content or albums.

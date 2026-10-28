# ADR 0014: Sharing Rules and Catalog Exchange

## Status

Accepted. Built and checked in S5 on 2026-10-10.

## Context

Trusting a node (ADR 0013) lets it talk to this node. It does not say what this node tells it. S5 had to decide what a trusted peer learns about the local catalog, how that is kept current, and how a node makes sense of what its peers offer.

The owner had already decided the default: a newly trusted peer sees nothing until something is shared with it, and pairing offers a one-click "share everything except **Hidden**" (plan decision 7).

## Decision

### What is shared

Sharing is by title: a whole series or a whole movie. Three things control it. (Since 2026-10-14 also by season and by episode, see the amendments.)

- **The Hidden list.** One built-in list. Anything on it is offered to nobody.
- **Share everything.** A switch on each peer. The peer is offered every title that is not hidden, including titles added later.
- **Share lists.** Named lists of titles, each given to chosen peers.

The rule, in order:

1. A peer that is not trusted is offered nothing.
2. A hidden title is offered to nobody.
3. A peer with "share everything" is offered the rest.
4. Any other peer is offered the titles on the lists it has been given.

A new peer has no lists and the switch off, so it is offered nothing (changed: the switch now starts on, see the amendment of 2026-10-16). The one-click option is a tick box on the invite: whoever redeems that invite gets "share everything" from the start. The redeemer's own sharing is not affected, because each side decides for itself.

Files the Arr app has stopped reporting are never offered.

### Filtering happens before sending

The offer for a peer is built on the sharing node, for that peer alone. A peer never receives an item together with an instruction not to show it.

An offered file carries what it is and how good it is: series or movie IDs (TVDB, TMDb, IMDb), title, year, season and episode numbers, episode titles, quality, resolution, size, languages, custom formats, release group and codecs. It carries a release title, which is the scene name if the Arr app kept one and otherwise the file name without its folder or extension.

It does not carry the file's path, the Arr app's address, or which application it came from. The file's identifier in the offer is opaque to the receiver.

### How a catalog is exchanged

A trusted peer asks for the catalog over the control channel. The sharing node answers with that peer's offer.

- The offer has a version, a hash of its contents. The asker sends the version it already holds, and an unchanged offer is answered with one short message.
- A large offer is sent in pages of 500 files. The first page takes a snapshot, and later pages come from that snapshot, so a change to sharing in the middle cannot produce a mixed result. If the snapshot is replaced before the asker finishes, it is told to start again.
- The receiver replaces everything it holds for that peer in one step. Whatever the peer stopped offering is gone.

A node fetches a peer's catalog when the peer becomes trusted, every hour, on demand, and when the peer says its sharing changed. (Since 2026-10-14: at the Catalog Sync Interval, and told only of removals, see the amendments.) That last message is only a hint and carries no content: each peer still has to ask, and is answered for itself. It exists so that something unshared disappears from peers within seconds, not within the hour.

A node drops everything it holds from a peer when trust ends in either direction, when the peer refuses the request, and when the peer is deleted.

### Comparing with the local catalog

Each offered file is marked against the local catalog:

| Mark | Meaning |
| --- | --- |
| Not in library | No local Arr app has the series or movie. |
| Missing | A local Arr app has it, without a file. |
| Upgrade | There is a local file, and the peer's is better. |
| Have | There is a local file at least as good. |

Matching is by ID only, never by title: TVDB first for series and TMDb first for movies, then the other IDs. A file holding several episodes takes the least favourable mark of its episodes.

"Better" here means a higher resolution, or the same resolution from a better source. It is a display aid. The real decision about what to grab is made by [Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/) with their own quality profiles when they search Nodearr (plan decision 2).

The marks are worked out again whenever the local catalog is refreshed.

## Evidence

- 47 unit tests for sharing and the peer catalog, and 6 more for peers. The sharing rules run against a real SQLite database: nothing by default, share everything, lists, **Hidden** overriding both, untrusted peers, missing files left out, and a serialized offer checked for paths.
- `acceptance/Run-S5Sharing.ps1` runs two real Nodearr instances against Sonarr and Radarr containers. 10 of 10 checks pass. It reads what node B actually holds after each change on node A, including that the raw response contains no path, and that revoking trust empties B's copy.
- The Sharing and Peer Catalog pages were used by hand against the same two instances. Hiding a series on one page changed the other node's page within seconds. The acceptance script drives the API and not the pages, so it did not catch a fault in the Sharing page's save, which the hand check did.

## Consequences

### Positive

- The default is private. Sharing anything is an explicit act.
- **Hidden** is absolute and simple to reason about: one list, no exceptions.
- Unsharing takes effect on peers almost at once.
- A peer learns nothing about how the sharing node's storage is laid out.

### Negative / trade-offs

- ~~Sharing is all of a series or none of it. There is no per-season or per-file control.~~ Done afterwards, see the amendments.
- A peer that has been offered a title knows it existed, and may have kept its own notes. Unsharing stops future offers. It cannot make a peer forget.
- A peer learns what this node has from what it is offered, and can infer a little from changes over time. That is inherent in sharing a catalog.
- The whole offer is rebuilt from the full local catalog on every request, and the receiver replaces every row for a peer on every change. This is simple and correct, and it will want a cheaper path for very large libraries.
- The same title held by two local Arr apps is offered twice, once per file.
- The release title falls back to the Arr app's file name, which Sonarr and Radarr parse well. S6 decides the final form.

## Amendments

- 2026-10-10, S6 (ADR 0015): an offered file no longer carries a release title made from the file name. It carries the name the release was grabbed under, when the Arr app kept one, and no part of the file name otherwise. The receiving node builds the title the Arr apps see. An offer also carries the edition, the dynamic range and the audio channels, which that title needs.

- 2026-10-10, S7 (ADR 0016): an offered file carries its extension, such as `.mkv`, and still nothing else of its name. Every request for a file is checked against the sharing rule when it arrives, so the follow-up below is done.

- 2026-10-11 (ADR 0015): an offered episode file carries the day each of its episodes aired, where the Arr app knows it. Sonarr searches for daily shows by day. Nothing else is added, and nothing of the path is sent still.

- 2026-10-13: **measured on a large library.** `LargeLibraryFixture` (run on purpose, not with the other tests) fills a real database with 1,500 series of 40 episodes and 6,000 movies, 66,000 files, shares all of it with one node, and times each path. On the development PC:

  | What | Time |
  | --- | --- |
  | Build the offer for a node shared everything | 0.6 s |
  | Answer every page of a catalog request | 0.6 to 0.9 s. 132 pages of 281 KB, 36 MB in all |
  | Receive a node's whole catalog and store it | 6.4 s |
  | Receive it again, unchanged | 0.4 s |
  | Compare every peer file with the library after a refresh | 0.7 s |
  | Check one file request against the sharing rule | 7 ms, was 0.65 s |
  | The indexer: by ID, a grab, the newest 100 | 1 to 63 ms |
  | The indexer: a text search | 0.26 s |

  The rebuild described above is cheap enough, and was left alone. Checking a single request is no longer done by building the whole offer. It reads the file's own rows and asks the same rule, and a test holds it to the same answer as the offer for every file, whatever is shared and whatever state the catalog is in.

  What was not changed: a changed offer is sent whole (since changed, see the amendment of 2026-10-14 on catalog changes). At this size that is 36 MB to each trusted node, each time anything shared changes, which with new episodes arriving daily is a few times a day. A page is far under the 4 MB message limit. Sending only what changed, or compressing pages, is the next step if that matters.

- 2026-10-14: **sharing by season and by episode, the way Sonarr monitors.** The owner's direction was to work with the Arr ecosystem: in Sonarr a whole series, a season or single episodes can be monitored, so a share list, and the **Hidden** list, can now hold any of those.
  - A whole series covers every episode, including those that arrive later. A season covers every episode in it, including later ones. An episode covers itself. A movie is always whole.
  - An episode is shared with a peer when its series, its season or the episode itself is on a list given to the peer, or the peer has "share everything", and none of those is on the **Hidden** list. Seasons are TVDB's, as in Sonarr.
  - A file that holds several episodes is offered only when every one of them is shared, as Sonarr wants a release only when every episode in it is monitored ("One or more episodes is not monitored").
  - A season or episode under a whole series on the same list adds nothing, and is dropped when the list is saved. Existing lists hold whole series and movies, and mean what they meant.
  - The Sharing page opens a series into its seasons and episodes. The boxes work like Sonarr's monitor toggles: a partly ticked series or season shows as such, and unticking part of something ticked whole keeps the rest ticked one level down. Episodes with no file yet are listed too, and marked.
  - Checked by unit tests, by `acceptance/Run-S5Sharing.ps1`, which now shares one episode, then a season, and hides one episode, then a season, from a list that shares the whole series, and by hand on the page against a real Sonarr's Pingu, 162 episodes in 7 seasons.

- 2026-10-14: **only what changed is sent, and each file carries its hash.** The owner's direction: a changed catalog is sent as its changes, not whole, and each file's hash goes in the offer so a node can tell when two peers hold the same bytes.
  - Each offered file has a fingerprint, a hash of everything the offer says about it. The catalog's version is a hash of every file's ID and fingerprint, nothing else. A node that kept each file's fingerprint can therefore work out the version of what it holds, and check it against what the peer says. The version used to be a hash of the whole offer. Changing how it is made costs every node one full fetch after upgrading.
  - An offered file carries its [BLAKE3](https://github.com/BLAKE3-team/BLAKE3) hash where the sending node has worked it out (ADR 0016, background hashing), and only if the hash was worked out for a file of the size the Arr app reports. The hash is part of the fingerprint, so a file hashed later reaches peers as a change.
  - The sending node keeps a record for each peer, in its database: the version it last sent and each file's fingerprint. A node that says it can take changes, and holds the version that record names, is sent only the files added or changed since and the IDs of the files removed. The record survives restarts.
  - The whole catalog is sent instead when the asker is an older node, when the sending node has no record or the asker holds another version, and when more than half of a catalog of more than one page changed, since that is about as large.
  - The receiving node applies the change, then works out the version of what it holds and counts it. If either does not match what the peer said, it fetches the whole catalog at once. A whole catalog that does not add up is kept, since it is what the peer sent, and the next sync fetches the whole of it again.
  - Measured with `LargeLibraryFixture`, 66,000 files: one upgraded episode reached the other node as 809 bytes in about a second, where the whole catalog is 38 MB. Checked by a round-trip test of both ends through the bytes they send, and by `acceptance/Run-S5Sharing.ps1`, where hiding a series reaches the peer as two removals and nothing else.

- 2026-10-14: **the Catalog Sync Interval, and only removals pushed.** Designed on the Arr apps' conventions, as the owner directed:
  - A node asks each trusted node what changed in its catalog every Catalog Sync Interval, a setting on **Settings > Node**. It works as Sonarr's RSS Sync Interval does: in minutes, 15 by default, 10 to 120, or 0 to turn it off, with Sonarr's own words when a value is refused. It was every hour. Asking is cheap now that only changes are sent.
  - A node tells a peer at once only when something the peer holds is no longer offered to it: unshared, hidden, or gone from the Arr app. It works this out by comparing what it would offer the peer now with its record of what it last sent. Something newly shared waits for the peer's next Catalog Sync, as a new release waits for Sonarr's next RSS Sync, or for a sync started by hand. It used to tell every peer of every change.
  - The whole library is read from each Arr app every 12 hours, as often as Sonarr refreshes its series. It was every 6 hours. The webhook from the Arr apps (planned) will bring single changes in between.
  - Checked by unit tests of the schedule, the setting and the notifier, and by `acceptance/Run-S5Sharing.ps1`: a newly shared file is not pushed, and arrives when the peer syncs. The scripts that wait for newly shared files now ask the peer to sync, as a user would.

- 2026-10-15: **"Better" is decided by the local quality profile.** An offered file is an upgrade only when the quality profile the local Arr app gives that series or movie would take it in place of the local file: its quality is allowed, ranks above the local file's quality in the profile's order, upgrades are on, and the local file is below the profile's cutoff. Qualities in one group count as equal, as the Arr apps treat them. Where several local apps hold a copy, it has to be better under each one's profile. Custom formats are left to the Arr app, which judges them when it searches (plan decision 2).
  - Profiles are read with their order of qualities, lowest first as Sonarr and Radarr list them, and the cutoff as a rank. Checked against a real Radarr 6.4.4: its default HD-1080p profile came back with the WEB 1080p group at one rank, the cutoff at Bluray-1080p, and upgrades off, so nothing is an upgrade under it, as Radarr would have it.
  - Until a profile is next read after updating, the old rule stands in: a higher resolution, or the same resolution from a better source.

- 2026-10-16: **one Hidden list.** The **Hidden** list is made the first time the lists are asked for, and two requests at once, such as a friend's catalog request and the Sharing page, could each make one. A node was found with two. Everything on either was still kept from everyone, but the second was easy to miss. It is now made under a lock, and migration 250 moves whatever was on a second **Hidden** list to the first, deletes the rest, and adds a unique index so the database holds at most one.

- 2026-10-16: **sharing everything is the default.** The owner's direction: most friends will want to share their libraries, so it is opt out, not opt in (plan decision 7, changed). The rule above is unchanged; only the switch's starting position is.
  - An invite shares everything except **Hidden** with whoever uses it, unless its tick box is unticked. It was unticked.
  - The node that uses an invite now chooses too, with the same tick box when it pastes the code, ticked. It used to share nothing with the inviter until set. Using another invite between two nodes that already trust each other keeps what was chosen before, on both sides. The inviting side used to take the new invite's box, which with the box ticked by default would have started sharing everything with a friend kept to lists.
  - A send to someone new (ADR 0021) shares everything except **Hidden** too, unless the sender unticks it, and then only what was sent.
  - Friends paired before keep what they have. Nothing is changed for them without the owner.
  - Checked by unit tests and `acceptance/Run-S5Sharing.ps1`, which now unticks the box to check that a friend paired that way is offered nothing.

- 2026-10-16 (ADR 0021): **sending.** A list can be made for one friend, to hold what is sent to them ("Sent to" and their name). It is an ordinary list in every other way, and the rule is unchanged: the **Hidden** list still wins over it.

- 2026-10-17: **everything, by app.** The owner's direction: share everything was all or nothing, where an owner may well share everything but [Whisparr](https://whisparr.com/). Rule 3 now reads: a peer marked "share everything" is offered the rest, apart from the Arr apps left out for it.
  - Each peer holds the IDs of the apps left out for it (`Peers.ExcludedApplicationIds`, migration 254). The Sharing page has a tick per app under each friend.
  - Lists are unchanged and still share from any app, so a few [Whisparr](https://whisparr.com/) items can go to a friend whose "everything" leaves Whisparr out. The **Hidden** list still wins over both.
  - Which apps a new friend gets is a setting on each app, **Share With New Friends**, on for every app but Whisparr v2 and v3. It applies when a friendship forms, on either side, and when an app is added: one with it off is left out for every friend there already. An app removed is taken off every friend's list, so a later app given its number is not left out by mistake.
  - Adult content is so opt in for "everything", as it already was for the Arr apps (ADR 0015): a Whisparr library reaches a friend only when it is ticked for them or put on a list. A per-kind switch (TV, movies, music, adult) was weighed and not taken: the apps are what the owner sees and configures, and two Radarrs, one for 4K, can be told apart.
  - Friends paired before keep what they had: every app. Nothing is taken from them without the owner.
  - Checked by unit tests (`SharingServiceFixture`, `ShareDefaultsFixture`, `PeerServiceFixture`).

- 2026-10-19: **removal notices are best effort.** `SharingNotifier` tries to tell each affected friend to sync, and logs a failed delivery. The friend's cached offer can remain until its next successful sync. The file request is checked against the current sharing rule either way. With the Catalog Sync Interval at zero, an undelivered notice needs another sync trigger, such as **Sync Peer Catalogs**.

- 2026-10-19: **Have means no upgrade under the local profile.** Since the quality-profile amendment, Have does not always mean the local file has the higher quality. Upgrades may be off, the cutoff may be met, or the offered quality may be disallowed. These marks do not filter indexer search results.

- 2026-10-23: **a peer's catalog has a limit.** A node fetching a peer's catalog believed the number of pages the peer said, up to 10,000, and kept every item of every page, however many ([#33](https://github.com/Nodearr/Nodearr/issues/33)). Past 10,000 pages it stopped quietly and took what it had for the whole catalog. A node takes at most 200,000 files from a peer now, three times the library measured above, in at most 400 pages of at most 500. A peer that says more, or sends more on a page, is refused at once: nothing of that fetch is kept, the catalog held before stays, and the peer's catalog error says why. A genuine node never sends more than a page holds.

- 2026-10-25: **shared catalog details.** For [#64](https://github.com/Nodearr/Nodearr/issues/64), the owner includes bounded series, episode, movie, film and scene descriptions, plus title genres, network and runtime where its Arr app reported them, in the same per-peer offer already filtered by sharing rules. The receiving node stores these details with the offer and serves cards from that copy, without accessing the owner's Arr API. A multi-episode offer keeps each episode's description by position, and the existing file resolution is displayed on the card. No file path is sent. Lidarr card design remains separate in #68.

  Checked with catalog, sharing, peer-catalog and controller fixtures, including Whisparr v2 scenes and v3 films/scenes. The existing S5 run passed all 19 checks against real Sonarr and Radarr, including details and resolution after peer exchange. A browser pass opened local and received series, episode and movie cards from table entries and posters. Series cards list every available resolution rather than taking one file as representative.

- 2026-10-27: **refresh details discarded by older receivers** ([#95](https://github.com/Nodearr/Nodearr/issues/95)). The offer already carries shared descriptions and genres. A receiver predating migration 260 could discard them while keeping the sender's version and fingerprints. Adding the columns did not invalidate that version, so an unchanged offer never filled them in. Migration 265 clears only `Peers.CatalogVersion`, requesting one full offer at the next successful sync. Cached files, fingerprints, sharing choices, `FirstSeen` and `CatalogFirstSyncedAt` stay in place; receiving old files again does not announce them as new RSS arrivals. Episode resources now include the parent series genres, network and runtime on both local and peer cards. The wire format is unchanged; a sender that never supplied these fields still shows less.

  Reproduced on the two-node UI kit before changing the code, with a genuine 0.5.0 sender and a receiver cache seeded to the older shape. After migration, the same sender version and fingerprints restored series, episode and movie metadata, with arrival timestamps unchanged. The `peerMetadata` browser check compares series and movie resources against the sender's local cards, checks the selected episode description/date/resolution against its local card, and checks visible metadata on all three cards. Felix's Arr data had no network; a controller fixture covers a supplied network. Migration and peer-catalog fixtures cover the invalidation and preserved arrival baseline.

## Follow-up work

- ~~S6: turn peer catalog rows into indexer results, with the opaque file identifier inside a signed GUID.~~ Done: ADR 0015.
- ~~S7: authorize every transfer request against the same sharing rule, so a file that is no longer offered cannot be fetched with an old identifier.~~ Done: ADR 0016.
- ~~A cheaper update path for large catalogs, if it is needed. Measured on 2026-10-13, see the amendments: building is cheap, and the cost that remains is sending a large changed offer whole.~~ Done: only what changed is sent, see the amendment of 2026-10-14.
- 2026-10-24: **a later revision is an upgrade** ([#51](https://github.com/Nodearr/Nodearr/issues/51)). What a friend has is an upgrade, as before, when the local Arr app would take it, and that now includes a PROPER or a REPACK of the quality the local file has. Sonarr's rule, which the other Arr apps share: the same quality at a later revision is taken even past the cutoff and with upgrades turned off, and a REAL outranks a later version. It is not an upgrade when the app's Propers and Repacks setting is "Do not upgrade" or "Do not prefer", which Nodearr reads with the quality profiles. Before an app's profiles are read, the simple rule counts it too, as the apps' default does.

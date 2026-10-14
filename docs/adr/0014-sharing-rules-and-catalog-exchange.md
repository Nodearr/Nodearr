# ADR 0014: Sharing Rules and Catalog Exchange

## Status

Accepted. Built and checked in S5 on 2026-10-10.

## Context

Trusting a node (ADR 0013) lets it talk to this node. It does not say what this node tells it. S5 had to decide what a trusted peer learns about the local catalog, how that is kept current, and how a node makes sense of what its peers offer.

The owner had already decided the default: a newly trusted peer sees nothing until something is shared with it, and pairing offers a one-click "share everything except Hidden" (plan decision 7).

## Decision

### What is shared

Sharing is by title: a whole series or a whole movie. Three things control it.

- **The Hidden list.** One built-in list. Anything on it is offered to nobody.
- **Share everything.** A switch on each peer. The peer is offered every title that is not hidden, including titles added later.
- **Share lists.** Named lists of titles, each given to chosen peers.

The rule, in order:

1. A peer that is not trusted is offered nothing.
2. A hidden title is offered to nobody.
3. A peer with "share everything" is offered the rest.
4. Any other peer is offered the titles on the lists it has been given.

A new peer has no lists and the switch off, so it is offered nothing. The one-click option is a tick box on the invite: whoever redeems that invite gets "share everything" from the start. The redeemer's own sharing is not affected, because each side decides for itself.

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

A node fetches a peer's catalog when the peer becomes trusted, every hour, on demand, and when the peer says its sharing changed. That last message is only a hint and carries no content: each peer still has to ask, and is answered for itself. It exists so that something unshared disappears from peers within seconds, not within the hour.

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

"Better" here means a higher resolution, or the same resolution from a better source. It is a display aid. The real decision about what to grab is made by Sonarr and Radarr with their own quality profiles when they search Nodearr (plan decision 2).

The marks are worked out again whenever the local catalog is refreshed.

## Evidence

- 47 unit tests for sharing and the peer catalog, and 6 more for peers. The sharing rules run against a real SQLite database: nothing by default, share everything, lists, Hidden overriding both, untrusted peers, missing files left out, and a serialized offer checked for paths.
- `acceptance/Run-S5Sharing.ps1` runs two real Nodearr instances against Sonarr and Radarr containers. 10 of 10 checks pass. It reads what node B actually holds after each change on node A, including that the raw response contains no path, and that revoking trust empties B's copy.
- The Sharing and Peer Catalog pages were used by hand against the same two instances. Hiding a series on one page changed the other node's page within seconds. The acceptance script drives the API and not the pages, so it did not catch a fault in the Sharing page's save, which the hand check did.

## Consequences

### Positive

- The default is private. Sharing anything is an explicit act.
- Hidden is absolute and simple to reason about: one list, no exceptions.
- Unsharing takes effect on peers almost at once.
- A peer learns nothing about how the sharing node's storage is laid out.

### Negative / trade-offs

- Sharing is all of a series or none of it. There is no per-season or per-file control.
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

  What was not changed: a changed offer is sent whole. At this size that is 36 MB to each trusted node, each time anything shared changes, which with new episodes arriving daily is a few times a day. A page is far under the 4 MB message limit. Sending only what changed, or compressing pages, is the next step if that matters.

## Follow-up work

- S6: turn peer catalog rows into indexer results, with the opaque file identifier inside a signed GUID.
- S7: authorize every transfer request against the same sharing rule, so a file that is no longer offered cannot be fetched with an old identifier.
- A cheaper update path for large catalogs, if it is needed. Measured on 2026-10-13, see the amendments: building is cheap, and the cost that remains is sending a large changed offer whole.

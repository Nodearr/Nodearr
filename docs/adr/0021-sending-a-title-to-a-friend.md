# ADR 0021: Sending a Title to a Friend

## Status

Accepted. Two slices, both built on 2026-10-16: sending, and the **Get it** card that adds what was sent to the friend's Arr app.

## Context

Sharing so far works one way round. The owner of a library decides what each friend is offered, and the friend's Arr apps find it when they search. That suits a library shared for good. It does not suit the most common way two people start: "I have that series, let me send it to you." The sender wants to hand over one thing. The receiver may not have it in [Sonarr](https://sonarr.tv/) at all, so Sonarr would never search for it, and may not even be a friend yet.

The owner's direction:

- a sender picks one title, or parts of it, and a friend, or someone new with an invite;
- the receiver confirms, with a **Get it** button, before anything is added to their apps;
- whatever can be shared can be sent, including single albums, which is why albums became shareable one by one first (ADR 0020, amendments).

## Decision

### Sending is sharing, plus a note

What is sent is shared with the friend on a list of their own, named "Sent to" and their name, made the first time they are sent something and kept from then on. The list is an ordinary share list: the Sharing page shows it, the owner can change or delete it, and the **Hidden** list still wins over it. Sharing it is what lets the friend's node fetch the files. Nothing about transfers or the sharing rule changes.

Then the friend's node is told, with a new control message, `sent`. It carries an ID for the send, the title as the sender's library names it, which part of it ("Seasons 1 and 2", "S01E04", an album's title, or none for the whole title), whether the whole title was sent, and the IDs the sender's offer gives the files it covers. It carries nothing the friend's catalog does not already hold, apart from what **Get it** needs (below): the IDs the Arr apps know the title by (TVDB, TMDb, IMDb, [MusicBrainz](https://musicbrainz.org/), and for adult content StashDB, ThePornDB and [Whisparr](https://whisparr.com/)'s own), its year, the series type the sender's Sonarr gives it, whether it is adult content, and the parts sent by the numbers every node shares: TVDB's season and episode numbers, and MusicBrainz release groups for albums.

- One title at a time: one series, movie, Whisparr item or artist, whole or in part, at the levels the Arr apps monitor (ADR 0014, amendments; ADR 0020, amendments).
- A send is refused when nothing of it can be offered: no file of it yet, or it is on the **Hidden** list.
- The receiving node takes the note only from a trusted node, keeps it, and fetches the sender's catalog again at once, so what was sent is there without waiting for the next Catalog Sync. The same send told twice is kept once. Text that would not do is dropped, IDs that are not offer IDs are dropped, and more than 200 notes waiting from one friend are not kept.
- A node that cannot be told yet, because it is offline, still finishing a pairing, or an older Nodearr that refuses the message, is told at the next connection check that reaches it, every 15 minutes. After 30 days it stops being tried. What was sent is shared with it either way, and shows in its catalog.
- Forgetting a send leaves what it shared on the list. Deleting the friend deletes their list and their sends, as it deletes everything else about them.

### Sending to someone new

A send to someone who is not a friend yet makes an invite of its own (ADR 0013), labelled "For" and the title. It was made to share nothing else. Since sharing everything except **Hidden** became the default (ADR 0014, amendment of 2026-10-16), it does that, as any invite does, unless the sender unticks it, and then it shares only what was sent. The send waits for the invite to be used.

When it is used, the inviting node attaches the send to the new friend and shares it before it answers the pairing, so the first catalog the new friend asks for already holds what was sent. The note follows a few seconds later, once the other side has finished pairing, as the first connection check does (`PeerConnectionUpkeep.PairingSettle`). Forgetting a send whose invite was not used deletes the invite.

### The receiving side: the Get it card

**Home** shows each note, "Alice sent you Pingu - Season 1", above everything else, whether or not setup is finished: someone who joined through a send may not have connected an app yet. The card opens the sent title's details, can be put away, and offers **Get it**. Nothing is added to an Arr app until the person presses it.

**Get it** does what the person would do in the Arr app, then lets the app do the rest:

1. **The app.** A series goes to Sonarr, a movie to [Radarr](https://radarr.video/), an artist or album to [Lidarr](https://lidarr.audio/), and a film or a scene to Whisparr v3, never to Radarr. With several of one kind, such as a 4K Sonarr, the card asks which. Whisparr v2 cannot be added to yet (follow-up work below); the card says so, and Whisparr finds the title from the friend when it searches.
2. **Adding, if the app does not have it.** It is looked up by its ID in the app, as the app's own Add New does (Whisparr v3: `stash:`, then `tpdb:`, `tmdb:` and `imdb:`, the prefixes its search takes, which return a scene or a film as a movie), with the choices the app asks for filled in the way most of the library already is:
   - the root folder: the only one, or the one most of the app's titles are in;
   - the quality profile: the one most of the app's titles use;
   - Lidarr's metadata profile: the app's first;
   - Sonarr's series type: the one the sender's Sonarr gives the series, else Standard; season folders on, as Sonarr's own default.

   The card shows each, to change before pressing it. A root folder or profile the app does not have is refused, so a request cannot point the app anywhere else.
3. **Monitoring what was sent, and nothing more.** A whole series or artist is monitored whole, with what arrives later. Seasons are monitored as seasons, so their later episodes are too. Single episodes and single albums are monitored one by one, once the app has read the title from its metadata server. Nodearr waits for the app's own refresh of a title it has just added to finish, since the app sets what is monitored as that ends. Lidarr leaves an artist added without its albums monitored unmonitored itself, which would keep its albums from being searched for on their own, so Nodearr monitors the artist again.
4. **Searching** for what was sent, and only what the friend offers of it now: in Sonarr an Episode Search for each such episode it does not have, in Radarr and Whisparr v3 a Movie Search, in Lidarr an Artist or Album Search. Not Sonarr's Season or Series Search: for a season that ended long ago those take only season packs (Sonarr's Season Search Maximum Single Episode Age), and a friend offers episode files one by one. The first live run found this: a Season Search for Pingu's first season, from 1990, turned the episodes down. The search reaches Nodearr like any other, finds the files in the friend's catalog, and the transfer and import follow as for any grab (ADR 0015, ADR 0016). The app may also find it from its other indexers. That is the app's choice, as in any search.

It runs in the background as a command. The card shows it working, then what was done ("Added to Sonarr, monitoring Season 1, and searching") or why it was not, with a way to try again. A note from an older sender, without IDs, is filled in from the friend's catalog once it has arrived.

## Amendment: already-held notifications (#102, 2026-10-28)

Pending received sends are compared against current local holdings using the same `LocalHoldings` classifier as the peer catalog, including the app's quality profile and PROPER/REPACK preferences. All distinct file IDs in the note must still resolve to offers from that sender of the same media kind. An incomplete or withdrawn offer cannot prove a send is held and supplies no counts. The comparison covers the sent snapshot, not unrelated files or another friend's copy. A multi-episode file is new until all its episodes are held.

When every sent file is **Have**, Home says **You already have it.** without requesting a Get it plan or showing its form. Details and Put away remain. Otherwise Home's Get it section shows the number of new files and upgrades. Working operations keep their progress, and successful operations retain their existing cleanup rules. Sender delivery remains **Told**, without a new protocol acknowledgement.

An already-held notification expires 24 hours after its first visible display in an active Home tab. This gives the person time to read it and avoids removing an unseen send just because Home polled in the background. Home acknowledges visibility explicitly; `AlreadyHeldSeenAt` is stored across restarts by migration 266. Polling and later visits do not reset it. If current holdings no longer prove the send is held, the timestamp is cleared and Get it remains available. Put away dismisses immediately. Missing, dismissed and untrusted notes cannot be acknowledged.

Verification on Windows: the solution built with analyzers and no warnings; 6,635 .NET tests passed (35 existing skips), along with 13 transport tests and all four frontend checks. After the final acknowledgement adjustment, all 172 sharing tests passed again. The two-node UI kit sent a held movie, a held episode, a partly held series and a PROPER movie upgrade through the real transport. Both themes showed the expected actions and counts; offscreen cards remained unseen, a hidden-document visibility check deferred acknowledgement, and revisits preserved the first display time. Put away removed the notification. A separate visible card expired after only its isolated database timestamp was aged past 24 hours and the node restarted; the machine clock was unchanged and the sender stayed Told. Existing received-details checks passed for whole series, seasons, multiple episodes, individual episodes and movies. The guide screenshots and V5 API description were regenerated from this build. This run did not download new files or exercise live Lidarr or Whisparr instances.

## Evidence

- Unit tests: `SendServiceFixture` (the friend's own list, what each part is called, refusals, the note and its retries, invites, forgetting, deleting a friend), `ReceivedServiceFixture` (keeping, checking and capping what arrives) and `PeerServiceFixture` (the `sent` message, and the invite named when it is used).
- Live on 2026-10-16, two Lidarr 3.1 instances and two fresh nodes on one machine:
  - Node A sent the album Spiderland to someone new. The send waited on an invite labelled "For Slint" that shared nothing else. Node B used it, and within seconds A's send said Told, A had a "Sent to Node B" list holding the album, B's **Home** said "Node A sent you Slint - Spiderland", and B's peer catalog held that album and nothing else, as missing from B's Lidarr.
  - An album A had no files of was refused: "There is no file of that in your library to send yet".
  - The whole artist, sent to B as a friend, was not told for a moment, then Told, and the list held the whole artist in place of the album.
  - The same from the Sharing page, narrowed to one album by unticking the other, and put away from B's **Home**.

- Live on 2026-10-16, **Get it**, with two Sonarr 4.0 and two Lidarr 3.1 instances and two fresh nodes. Node A had Pingu's first three episodes and Spiderland. B's Sonarr had no Pingu and B's Lidarr no Slint. A sent Pingu Season 1 to someone new, Spiderland to B once they were friends, and S01E03 alone. B pressed **Get it** on each card with the choices it offered:
  - Season 1: "Added to Sonarr, monitoring Season 1, and searching for 26 episodes". Season 1 was monitored, all 26 episodes of it, and no other season. Sonarr grabbed E01 to E03 through Nodearr, and they were fetched from A and imported.
  - Spiderland: "Added to Lidarr, monitoring Spiderland, and searching". Spiderland was monitored, Tweez not, and the artist was. All six tracks arrived and were imported.
  - S01E03: "Already in Sonarr, monitoring 1 episode, and searching for 1 episode".
  - With Pingu deleted from B's Sonarr, a send of S01E02 alone added Pingu with that one episode monitored and fetched it, pressed from the card on **Home**.
  - Radarr and Whisparr v3, two of each, the same way: A sent Night of the Living Dead to someone new and a scene, found in Whisparr by its StashDB ID, to B once they were friends. B pressed **Get it** on each card: "Added to Radarr, monitored, and searching" and "Added to Whisparr, monitored, and searching". Both apps grabbed through Nodearr, and both files were fetched from A and imported. Radarr first turned the film down as a sample, because the test's file ran two minutes; with an eleven-minute file it imported it.
  - The runs found three faults, fixed before the last: Season Search turning single episodes down (above), Lidarr answering a bulk change with a list where Nodearr expected one object, and the indexer showing one result for identical bytes offered as different episodes. The test had made its three episodes from one file, and the indexer kept one of them, so the other two were never found. Copies are now one result only when they are offered as the same thing.

- 2026-10-16, by hand in the pages (docs/guides/ui-test.md): **Get it** on a whole series searched for every missing episode the series has, 156 for Pingu, each a search of every indexer the receiving Sonarr has, where the friend had three. It now searches for the episodes, or for a whole artist the albums, the friend offers, and the app's own monitoring looks for the rest as it always does.

## Consequences

### Positive

- "Let me send it to you" is one form: pick the title and the friend, or copy one invite code.
- Nothing new can be fetched that sharing would not allow: a send is a share, and every file request is still checked against the sharing rule (ADR 0016).
- A send to someone new costs them two steps: using the invite makes them a friend and brings what was sent, and **Get it** puts it in their app.
- **Get it** uses the app's own add, monitor and search, so what is added looks as if the person had added it there, and what the app does next is what it always does.

### Negative / trade-offs

- An older node is offered what was sent, but is never told. Its owner finds it only in the catalog.
- The list a friend's sends go on grows with every send until the owner prunes it. That is deliberate: a send is meant to be fetchable, and unsharing it silently would break a transfer under way.
- The note can only say what the sender's library calls the title. The friend's library may name it differently.

## Follow-up work

- ~~The **Get it** card.~~ Done, see above.
- ~~**Get it** for Whisparr v3.~~ Done: films and scenes, by StashDB, ThePornDB, TMDb or IMDb ID.
- **Get it** for Whisparr v2. Built on Sonarr, it adds a site and then monitors a scene in it, and a friend's catalog does not name the site by its ID in v2's metadata. A scene that came from a v2 is known only by v2's own number, which Whisparr v3 cannot look up; the card says so.

## Amendments

- 2026-10-27 ([#93](https://github.com/Nodearr/Nodearr/issues/93), [#94](https://github.com/Nodearr/Nodearr/issues/94)): **Get it on peer details cards and poster overlays.** Cards expose their current Get it selection alongside their other details: a series selects the whole title across trusted friends, an episode selects its containing file, and a movie, film or scene selects its file. A held selection has no action; upgrades remain actionable. The details refresh while open and suppress the action after a failed refresh, so a withdrawn offer or revoked friendship does not leave a usable action. The existing Get it service still validates the offer when planning and executing the request. There are no standalone season cards; the shared dialog retains its Season choice. Local cards keep Send in the same position. Peer posters use the same overlay style as local Send, with a download icon labelled "Get <title>"; keyboard focus and touch expose it too. Table actions keep their existing appearance.

- 2026-10-28 ([#105](https://github.com/Nodearr/Nodearr/issues/105)): **received sends open details over Home.** The former catalog link is **Details**, and it and the small poster open the existing peer card without navigation. A local endpoint resolves the received note's offer IDs against that sender's current cached catalog and checks trust on each request. It does not fall back to a different friend's copy or fetch more metadata from the friend. A single explicit episode opens at its index within the offered file; season and multiple-episode sends mark the selected episodes, which remain visible despite a hidden-missing preference. Both entry points use the same details mapper, availability and Get it action as catalog cards; Home keeps its original quick Get it for the exact send. Unavailable offers explain why details cannot open, revoked friends' cards disappear, and music details remain in #68. The note and offer wire formats and stored data are unchanged.

  Checked with the two-node UI kit and a genuine 0.5.0 sender: whole series, season, two episodes, one episode and a movie opened from both controls over Home, including the nested Get it dialog. An episode requested there imported into real Sonarr with bytes matching the sender. Withdrawing the movie replaced its controls with an explanation and returned 404 from the old endpoint; revoking trust removed the received cards and refused cached details. The test friendship and sharing were restored. API fixtures cover these resolutions, multi-episode file indexes, music, movie/film/scene kinds, withdrawn offers, dismissed notes and revoked trust. `receivedDetails` in the existing screenshot tool checks the browser behavior and refreshes the guide images.
  - Verified with an analyzer-enabled solution build, 15 API tests and 18 catalog Get it service tests, frontend lint, type checking, style checks and a production UI build. The UI kit imported an individual episode from its card, the rest of its season, a movie from its card, a movie through the phone poster icon, and a whole series through a keyboard-focused desktop icon. Their files matched the sources byte for byte. The final source build also preserved the series command anchor, hid Have actions, retained a real PROPER upgrade action, and removed actions from open cards after an offer was withdrawn or trust revoked. `tools/docs-screenshots.mjs catalog peerCards` checks selection scopes and refreshes the affected poster and card screenshots. These checks used two nodes on one Windows machine with direct connections.

- 2026-10-24: **Get it from the peer catalog** (#54). A file row or poster can start the same add, monitor and search workflow as a received send. Both use one service for the Arr app, choices, defaults and library request, and one form that keeps valid choices when its plan changes. A TV selection can cover its episode file, a season or the whole series; an album covers that album. Catalog selections are resolved from current offers of trusted friends, checked against the local library, and checked again when their command runs. A title already held offers no action; an upgrade is left to the Arr app's own search rules. The existing command queue and its live updates report progress where Get it was selected. Catalog requests do not create received-send cards on Home.
  - Offers did not carry the series type. They now optionally carry `anime` or `daily`; standard is omitted so its existing fingerprints stay unchanged, and older nodes still work. A cached older offer defaults to standard until it is updated.
  - Sonarr's shared add path previously searched only episodes with no file. It now also searches requested episodes offered as upgrades, leaving acceptance to Sonarr's own profiles. A held episode without an offered upgrade is still skipped.
  - The UI kit exposed overlapping catalog syncs inserting the same offers several times. Syncs and purges for one friend now run in order, and a waiting sync reads the current version before fetching. The open catalog refreshes its statuses, and the Get it dialog refreshes app choices while it is idle.
  - Verified on Windows with the solution build, 6,473 passing unit tests (35 skipped), and all four frontend checks. The catalog acceptance script imported a movie, episode, season and six-track album through real Radarr, Sonarr and Lidarr, checked their bytes and monitoring, and imported a PROPER upgrade. In the UI kit, a movie from Posters and an episode followed by a season from the catalog imported; app selection, unavailable-app advice, root-folder recovery, retained choices and reopening command progress were checked. Changing TV scope waits for the initial plan. Home's shared Get it form also imported three episodes. These runs used direct routes on one machine, not two networks; Whisparr routing was covered by unit tests rather than a new live Whisparr run.

- 2026-10-24 ([#53](https://github.com/Nodearr/Nodearr/issues/53)): **choices follow the current plan, and successful cards leave Home.** Refreshing a plan replaces only empty or removed root folders and profiles, including Lidarr's metadata profile, and preserves the person's valid choices. Before queueing an addition, Nodearr asks the app for its current choices and refuses a stale selection with a request to pick again. Existing titles keep their settings. Failures are logged as well as shown on the card. A successful card is marked handled once every offered file named in the send is classified **Have** against the current local catalog, or 24 hours after Get it succeeds, whichever comes first. A partial or withdrawn offer does not prove the files arrived. Home checks every 30 seconds while successful cards remain, as well as on reload. Failures stay until retried or put away. The success time is stored across restarts; older successful cards start their 24 hours at the database upgrade.

- 2026-10-19: **monitoring an existing title adds to what was chosen before.** The Sonarr and Lidarr adders turn monitoring on for what was sent without turning other monitored parts off. A new title starts with only the requested parts. A whole series means Sonarr's All Episodes choice, which leaves specials out; Season 0 or its episodes must be sent explicitly. Searching still uses the Arr app's enabled indexers and profiles, so it can choose a release from another indexer.

- 2026-10-19: **searching before matching offers are known.** Sonarr falls back to searching every missing episode of the requested parts when no offered episodes are known yet. For a whole artist, Lidarr falls back to Artist Search when no offered albums are known. Once offers are known, the limited searches described above apply.

### Amendment, 2026-10-28: music details and catalog actions (#68)

Received music sends resolve the same cached, trusted album offers used by the peer catalog. A whole artist opens its artist card; a single album opens directly; a selection of albums opens the artist with the sent albums marked. Withdrawing the offer removes its details and actions. No card calls the sender's Lidarr. Home's Get it still uses the exact sent selection.

Catalog artist requests use a new explicit `Artist` scope; existing music `File` and `Title` requests retain their album-only meaning. Artist requests monitor future albums and search only the sender's currently offered missing or upgradable albums initially. Album requests do not expand into whole-artist requests. Existing artist configuration is preserved. This extends the artist/album pipeline from ADR 0020 without adding song-level requests.

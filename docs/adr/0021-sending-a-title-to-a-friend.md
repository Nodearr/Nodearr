# ADR 0021: Sending a Title to a Friend

## Status

Accepted. Two slices: sending (built on 2026-10-16), and the Get it card that adds what was sent to the friend's Arr app (next).

## Context

Sharing so far works one way round. The owner of a library decides what each friend is offered, and the friend's Arr apps find it when they search. That suits a library shared for good. It does not suit the most common way two people start: "I have that series, let me send it to you." The sender wants to hand over one thing. The receiver may not have it in Sonarr at all, so Sonarr would never search for it, and may not even be a friend yet.

The owner's direction:

- a sender picks one title, or parts of it, and a friend, or someone new with an invite;
- the receiver confirms, with a **Get it** button, before anything is added to their apps;
- whatever can be shared can be sent, including single albums, which is why albums became shareable one by one first (ADR 0020, amendments).

## Decision

### Sending is sharing, plus a note

What is sent is shared with the friend on a list of their own, named "Sent to" and their name, made the first time they are sent something and kept from then on. The list is an ordinary share list: the Sharing page shows it, the owner can change or delete it, and the Hidden list still wins over it. Sharing it is what lets the friend's node fetch the files. Nothing about transfers or the sharing rule changes.

Then the friend's node is told, with a new control message, `sent`. It carries an ID for the send, the title as the sender's library names it, which part of it ("Seasons 1 and 2", "S01E04", an album's title, or none for the whole title), whether the whole title was sent, and the IDs the sender's offer gives the files it covers. It carries nothing the friend's catalog does not already hold.

- One title at a time: one series, movie, Whisparr item or artist, whole or in part, at the levels the Arr apps monitor (ADR 0014, amendments; ADR 0020, amendments).
- A send is refused when nothing of it can be offered: no file of it yet, or it is on the Hidden list.
- The receiving node takes the note only from a trusted node, keeps it, and fetches the sender's catalog again at once, so what was sent is there without waiting for the next Catalog Sync. The same send told twice is kept once. Text that would not do is dropped, IDs that are not offer IDs are dropped, and more than 200 notes waiting from one friend are not kept.
- A node that cannot be told yet, because it is offline, still finishing a pairing, or an older Nodearr that refuses the message, is told at the next connection check that reaches it, every 15 minutes. After 30 days it stops being tried. What was sent is shared with it either way, and shows in its catalog.
- Forgetting a send leaves what it shared on the list. Deleting the friend deletes their list and their sends, as it deletes everything else about them.

### Sending to someone new

A send to someone who is not a friend yet makes an invite of its own (ADR 0013), which shares nothing else: it is created without "share everything", and labelled "For" and the title. The send waits for the invite to be used.

When it is used, the inviting node attaches the send to the new friend and shares it before it answers the pairing, so the first catalog the new friend asks for already holds what was sent. The note follows a few seconds later, once the other side has finished pairing, as the first connection check does (`PeerConnectionUpkeep.PairingSettle`). Forgetting a send whose invite was not used deletes the invite.

### The receiving side

Home shows each note, "Alice sent you Pingu - Season 1", above everything else, whether or not setup is finished: someone who joined through a send may not have connected an app yet. For now the card links to the friend's catalog, and can be put away. The Get it card is the next slice.

## Evidence

- Unit tests: `SendServiceFixture` (the friend's own list, what each part is called, refusals, the note and its retries, invites, forgetting, deleting a friend), `ReceivedServiceFixture` (keeping, checking and capping what arrives) and `PeerServiceFixture` (the `sent` message, and the invite named when it is used).
- Live on 2026-10-16, two Lidarr 3.1 instances and two fresh nodes on one machine:
  - Node A sent the album Spiderland to someone new. The send waited on an invite labelled "For Slint" that shared nothing else. Node B used it, and within seconds A's send said Told, A had a "Sent to Node B" list holding the album, B's Home said "Node A sent you Slint - Spiderland", and B's peer catalog held that album and nothing else, as missing from B's Lidarr.
  - An album A had no files of was refused: "There is no file of that in your library to send yet".
  - The whole artist, sent to B as a friend, was not told for a moment, then Told, and the list held the whole artist in place of the album.
  - The same from the Sharing page, narrowed to one album by unticking the other, and put away from B's Home.

## Consequences

### Positive

- "Let me send it to you" is one form: pick the title and the friend, or copy one invite code.
- Nothing new can be fetched that sharing would not allow: a send is a share, and every file request is still checked against the sharing rule (ADR 0016).
- A send to someone new costs them one step: using the invite makes them a friend and brings what was sent.

### Negative / trade-offs

- An older node is offered what was sent, but is never told. Its owner finds it only in the catalog.
- The list a friend's sends go on grows with every send until the owner prunes it. That is deliberate: a send is meant to be fetchable, and unsharing it silently would break a transfer under way.
- The note can only say what the sender's library calls the title. The friend's library may name it differently.

## Follow-up work

- The Get it card: add what was sent to the receiver's Arr app if it is missing, monitor what was sent, and search. Root folder: the only one, or the one most of the library uses. Quality profile: the one most of the library uses. Other options, such as series type and season folders, copied from the sender's item where offered, else the app's defaults. All shown on the card to change before pressing it.

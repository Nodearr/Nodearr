# Friends and sharing

Two nodes become friends when one uses the other's invite. From then on each can be offered what the other chooses to share, and nothing else.

## Inviting a friend

On **Nodes > Invites**, or the last step on Home:

![Nodes > Invites, with a new invite labelled For Sam and its code ready to copy](images/invites.png)

1. Choose how long the invite works (**Valid For**, 24 hours unless you change it), and give it a **Label**, a note for yourself about who it is for.
2. **Share everything except Hidden** is ticked: this friend is offered your whole library from every app shared with new friends, which is every app but Whisparr unless you change it, apart from what is on your Hidden list, and including what you add later. Untick it if they should be offered only what you put on a list for them.
3. **Create Invite**, and copy the code. Send it to your friend however you like: a message, an email, read out over the phone.

An invite works once, for one node, and stops working when it expires. Nodes > Invites shows each one as open, used or expired.

The code holds your node's ID, how to reach it, the time it expires, and a secret. Treat it like a key to your door until it is used: anyone who has it before your friend can use it in their place. Your node stores only a hash of the secret.

## Using a friend's invite

Paste the code under **Redeem Invite** on Nodes > Invites, or on Home. **Share everything except Hidden** is ticked there too, for what your node offers them: untick it to share only lists with them. Your node connects to your friend's, proves who it is, and hands over the secret. When your friend's node accepts it, each of you appears on the other's **Nodes > Trusted Nodes** as trusted, under the name each of you chose.

If it fails:

- **"This invite has expired"** or **"did not accept this invite"**: it ran out, or it was used already. Ask for a new one.
- **"Unable to reach the node that created this invite"**: your node could not get through to your friend's. Both nodes need to be running and online. See [Troubleshooting](troubleshooting.md#a-friend-cannot-be-reached).
- **"turned away too many invites from this node lately"**: your node tried five codes in 15 minutes that did not work. Wait 15 minutes, then try once with a fresh code.

## Your friends

![Nodes > Trusted Nodes: this node, and Sam, trusted, sharing everything except Hidden, connected directly](images/trusted-nodes.png)

**Nodes > Trusted Nodes** lists every node you have paired with, with:

- **Status**: Pending (you used their invite and they have not answered yet), Trusted, Revoked or Blocked;
- **Connection**: **Direct**, meaning files can move, or **Relay only**, meaning the two nodes can talk but files cannot (see [Troubleshooting](troubleshooting.md#relay-only));
- **Sharing**: whether they are offered everything except Hidden, or only the lists they are on;
- **Last seen**.

What you can do depends on where trust stands:

| Status | What you can do |
| --- | --- |
| Trusted | **Check connection** (does it again now, and says whether files can move), **Revoke trust**, **Block** |
| Pending, Revoked | **Block**, **Delete** |
| Blocked | **Unblock** |

- **Revoke trust** ends the friendship on both sides at once. Any transfer between you stops, and each of you forgets what the other shared. To be friends again, one of you sends a new invite.
- **Block** does the same, and also turns that node away whenever it connects, before it reaches Nodearr. It cannot pair with you again, even with a new invite, until you unblock it. Unblocking does not restore trust: it takes a new invite.
- **Delete** forgets a node that is not trusted. A blocked node cannot be deleted, so it stays blocked.

## Choosing what to share

A new friend is offered everything except Hidden, from every app but Whisparr, unless you unticked that on the invite. On **Catalog > Sharing** you can change it for each friend.

### With a friend: everything, some apps, or lists

Under **Trusted Nodes** on the Sharing page, each friend has a line of their own:

![Catalog > Sharing: Sam shares everything except Hidden, from Radarr and Sonarr, and the form to send a friend something](images/sharing.png)

- **Share everything except Hidden** or **Stop sharing everything** turns "everything" on and off for them. Off, they are offered only the lists they are on.
- When it is on, a tick for each of your Arr apps says which apps "everything" takes in. Untick one to leave its whole library out for that friend. To share everything but Whisparr, leave Whisparr unticked.
- Whatever is on a list they are on is offered to them as well, from any app, ticked or not. To share all your series and films but only a few Whisparr items, leave Whisparr unticked and put those items on a list for them.

The label beside each friend says which it is: everything, everything but some apps, or only their lists. Nodes > Trusted Nodes shows the same.

**Which apps a new friend gets** is set on each app, in Settings > Apps: **Share With New Friends**. It is on for every app but Whisparr. It also decides what happens when you add an app later: one with it on is shared with every friend who is offered everything, and one with it off is left out for all of them until you tick it for someone.

### Lists

A list holds chosen things from your library, and is given to chosen friends:

1. **New List**, and name it.
2. Tick what goes on it. For a series you can tick the whole series, single seasons or single episodes. For an artist, the whole artist or single albums. Movies and Whisparr items go on whole.
3. Under **Shared with**, tick the friends who get it.
4. **Save**.

A whole series, season or artist takes in what arrives later: a new episode of a ticked season is shared as soon as Sonarr imports it, and so is a new album of a ticked artist, once it is complete. A season or album that arrives later is shared only if the whole series or artist is ticked. An album is offered only once Lidarr holds every track of it; the Sharing page says which are not there yet.

A file that holds several episodes is shared only when every one of them is, as Sonarr wants a release only when every episode in it is monitored.

### Hidden

Anything on the **Hidden** list is offered to nobody, whatever else says so: not to friends who are offered everything, not to friends whose lists hold it, not to someone you send it to. Use it for what you never want to share. It works at the same levels as a list, so you can hide one season of a series or one album of an artist and share the rest.

There is one Hidden list, and it is on Catalog > Sharing with the other lists, at the bottom of the page:

![The Hidden list, filtered to find a title to tick](images/sharing-hidden.png)

1. Find the title with **Filter**.
2. Tick it, or for a series some of its seasons or episodes, or for an artist some of its albums.
3. **Save**.

Within seconds it is gone from every friend's node, and a transfer of it stops. Untick it and **Save** to share it again, with whoever would be offered it otherwise. The Hidden list has no **Shared with** and cannot be deleted, because it applies to everyone.

A whole series or artist that is hidden stays hidden as it grows: a new episode or album of it is never offered. Your Arr apps are not told anything: Hidden is only about what your friends see.

### When sharing changes

- **Taking something back is immediate.** When you unshare or hide something, or your Arr app deletes it, each friend who was offered it is told at once and fetches your offer again. Within seconds it is gone from their node and their Arr apps can no longer find it. A transfer of it stops at once.
- **Sharing something new is not announced.** Each friend's node picks it up at its next catalog sync, every 15 minutes unless they changed it (Settings > Node), or straight away if they press **Sync Peer Catalogs** on Catalog > Peer Catalog.

Having been offered something earlier counts for nothing: every request for a file is checked against what is shared at that moment.

## Sending something to a friend

"I have that, let me send it to you": on **Catalog > Sharing**, under **Send to a friend**:

![Send to a friend, with Frieren chosen for Alex, and an earlier send to Alex marked Told](images/send-to-a-friend.png)

1. Find the title, and pick it. For a series you can then narrow it to seasons or episodes, and for an artist to albums, as on a list.
2. Under **To**, pick the friend. Or pick **Someone new, with an invite** if they are not a friend yet, and how long the invite works.
3. **Send**.

What you send is shared with that friend on a list of their own, **Sent to** and their name, which you will see among your lists and can change like any other. Their Nodearr is told, and fetches your catalog at once, so it is there for them straight away. Their Home shows **"<your name> sent you <title>"**.

If nothing of it can be sent, Nodearr says so: there is no file of it in your library yet, or it is on your Hidden list.

**To someone new**, Nodearr makes an invite for this and shows you the code. Send it to them. When they use it in their Nodearr, the two of you are friends, their Home shows what you sent, and they are offered the rest of your library except Hidden, as any new friend is. Untick **Share everything except Hidden with them too** first if they should be offered only what you sent. The invite is also listed on Nodes > Invites, labelled **For** and the title.

The **Sent** list under the form says how each send stands:

- **Told**: their Nodearr knows.
- **Shared, not told yet**: it is shared with them, and their Nodearr will be told the next time yours reaches it, within 15 minutes of it being online. An older Nodearr is never told, but finds what you sent in its catalog.
- **Invite not used yet**, or **Invite expired or deleted**.

The cross on a send forgets it. What it shared stays shared until you change the list. For a send whose invite was not used, it deletes the invite too.

## When a friend sends you something

Home shows it at the top, above everything else, as **"<name> sent you <title>"**.

![Home with Sam's send of Frieren at the top, its choices for Sonarr filled in, and Get it](images/received.png)

**Get it** adds it to your Arr app and fetches it:

1. If the app does not have it yet, Nodearr adds it, as you would on the app's Add New page. The card shows the choices it will use, filled in the way most of your library already is: the **Root Folder** most of it is in, the **Quality Profile** most of it uses, and for a series the **Series Type** your friend's Sonarr gives it (Lidarr also asks for a **Metadata Profile**). Change any of them first if you like.
2. It monitors what was sent, and nothing more: the whole series or artist, the seasons, the episodes, or the albums.
3. It has the app search for it. The app finds it from your friend, through Nodearr, and the download and import follow as for anything else. Activity > Transfers shows it arriving.

A series goes to Sonarr, a movie to Radarr, music to Lidarr, and a film or a scene to Whisparr v3. With two of one kind, such as a second Sonarr for 4K, the card asks which. If the app has it already, Get it only monitors what was sent and searches.

The card then says what was done, such as "Added to Sonarr, monitoring Season 1, and searching", or why it was not, with a way to try again. Nodearr cannot add to Whisparr v2 yet, nor add a scene that came from a friend's Whisparr v2 to Whisparr v3: add those yourself, and Whisparr finds them from your friend when it searches.

**See it in their catalog** opens Catalog > Peer Catalog. **Put away** clears the card.

### Hashing

Each file you share is read once ahead of time to work out its hash, so a friend who asks for it does not wait while a large file is read. Catalog > Sharing shows how far that has got. It pauses while your node is sending, and a file is read again only when it changes. The hash also lets a friend's node tell that two friends have the same file, and fetch it from the other if one goes away.

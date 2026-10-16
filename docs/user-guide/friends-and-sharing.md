# Friends and sharing

Two nodes become friends when one uses the other's invite. From then on each can be offered what the other chooses to share, and nothing else.

## Inviting a friend

On **Nodes > Invites**, or the last step on Home:

1. Choose how long the invite works (**Valid For**, 24 hours unless you change it), and give it a **Label**, a note for yourself about who it is for.
2. Tick **Share everything except Hidden** if this friend should be offered your whole library from the start. Otherwise they are offered nothing until you share something with them.
3. **Create Invite**, and copy the code. Send it to your friend however you like: a message, an email, read out over the phone.

An invite works once, for one node, and stops working when it expires. Nodes > Invites shows each one as open, used or expired.

The code holds your node's ID, how to reach it, the time it expires, and a secret. Treat it like a key to your door until it is used: anyone who has it before your friend can use it in their place. Your node stores only a hash of the secret.

## Using a friend's invite

Paste the code under **Redeem Invite** on Nodes > Invites, or on Home. Your node connects to your friend's, proves who it is, and hands over the secret. When your friend's node accepts it, each of you appears on the other's **Nodes > Trusted Nodes** as trusted, under the name each of you chose.

If it fails:

- **"This invite has expired"** or **"did not accept this invite"**: it ran out, or it was used already. Ask for a new one.
- **"Unable to reach the node that created this invite"**: your node could not get through to your friend's. Both nodes need to be running and online. See [Troubleshooting](troubleshooting.md#a-friend-cannot-be-reached).
- **"turned away too many invites from this node lately"**: your node tried five codes in 15 minutes that did not work. Wait 15 minutes, then try once with a fresh code.

## Your friends

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

Nothing is shared until you share it. On **Catalog > Sharing**:

### With a friend: everything, or lists

Each friend is either:

- offered **everything except Hidden** (from the invite, or the button on the Sharing page), or
- offered **only the lists they are on**.

### Lists

A list holds chosen things from your library, and is given to chosen friends:

1. **New List**, and name it.
2. Tick what goes on it. For a series you can tick the whole series, single seasons or single episodes. For an artist, the whole artist or single albums. Movies and Whisparr items go on whole.
3. Under **Shared with**, tick the friends who get it.
4. **Save**.

A whole series, season or artist takes in what arrives later: a new episode of a ticked season is shared as soon as Sonarr imports it, and so is a new album of a ticked artist, once it is complete. A season or album that arrives later is shared only if the whole series or artist is ticked. An album is offered only once Lidarr holds every track of it; the Sharing page says which are not there yet.

A file that holds several episodes is shared only when every one of them is, as Sonarr wants a release only when every episode in it is monitored.

### Hidden

Anything on the **Hidden** list is offered to nobody, whatever else says so: not to friends who are offered everything, not to friends whose lists hold it. Use it for what you never want to share. It works at the same levels as a list, so you can hide one season of a series or one album of an artist and share the rest.

### When sharing changes

- **Taking something back is immediate.** When you unshare or hide something, or your Arr app deletes it, each friend who was offered it is told at once and fetches your offer again. Within seconds it is gone from their node and their Arr apps can no longer find it. A transfer of it stops at once.
- **Sharing something new is not announced.** Each friend's node picks it up at its next catalog sync, every 15 minutes unless they changed it (Settings > Node), or straight away if they press **Sync Peer Catalogs** on Catalog > Peer Catalog.

Having been offered something earlier counts for nothing: every request for a file is checked against what is shared at that moment.

### Hashing

Each file you share is read once ahead of time to work out its hash, so a friend who asks for it does not wait while a large file is read. Catalog > Sharing shows how far that has got. It pauses while your node is sending, and a file is read again only when it changes. The hash also lets a friend's node tell that two friends have the same file, and fetch it from the other if one goes away.

# Nodearr overview

What Nodearr is, what its parts are, and what happens from adding an Arr app to importing a friend's file. Each part links to the decision record that explains why it works the way it does.

This describes Nodearr as it is built. The design it started from, written before any of it was, is in this file's history and in the [product definition](research/nodearr-product-definition.md).

## In one sentence

The Arr apps say what the media is. Nodearr says which trusted friend has an acceptable copy, and fetches it from them.

## What Nodearr is, and is not

Nodearr runs next to the Arr apps a person already has, and does two jobs for them:

- **It is an indexer.** The Arr apps search it as they search any Newznab indexer, and the results are what trusted friends share.
- **It is a download client.** The Arr apps hand it what they grab, as they would to [SABnzbd](https://sabnzbd.org/). It fetches the file directly from the friend who has it, checks it, and puts it where the Arr app imports from.

The Arr apps remain the source of truth for what the media is, which files are in the library, where they are, and how good they are. Nodearr reads all of that from them ([ADR 0006](adr/0006-arr-apps-as-catalog-source-of-truth.md)) and never scans folders to guess.

Nodearr is not a shared drive, a media server, a public indexer or a library manager. Nothing of a library goes to anyone the owner has not chosen, and media files only ever travel directly between two machines.

## The parts

```text
 Your machine                                        A friend's machine
 ------------                                        ------------------
 Sonarr, Radarr, Whisparr, Lidarr                    Their Arr apps
   |  (Arr APIs, webhooks)   ^  (Newznab, SABnzbd)        |
   v                         |                            v
 Nodearr  ------------------------------------------>  Nodearr
   |   catalog, sharing, indexer, download client         |
   v                                                       v
 nodearr-transport  <====== direct QUIC (iroh) =====>  nodearr-transport
                       \                         /
                        `-- n0's relay: finding --'
                            each other only
```

- **Nodearr** is the application: a [.NET](https://dotnet.microsoft.com/) service with a web UI, derived from [Sonarr](https://sonarr.tv/) ([ADR 0004](adr/0004-technology-stack.md), [NOTICE](../NOTICE.md)). It talks to the Arr apps, keeps the catalogs, applies the sharing rules, answers searches and runs transfers.
- **`nodearr-transport`** is a small [Rust](https://www.rust-lang.org/) program Nodearr starts beside itself. It holds the node's key and does all the networking between nodes, with [iroh](https://www.iroh.computer): encrypted QUIC connections, NAT traversal, and the file transfer protocol ([ADR 0009](adr/0009-iroh-behind-transfer-abstraction.md), [ADR 0012](adr/0012-custom-transfer-protocol-instead-of-iroh-blobs.md)). Nodearr talks to it over a loopback port with a token only the two of them know ([ADR 0013](adr/0013-node-identity-pairing-and-trust.md)).
- **The relay** helps two nodes find a path to each other. By default it is one of n0's public relays. A person can run their own, or turn relays off. It may carry pairing, connection checks and ending trust, encrypted, but nothing of what a library holds: no catalog, poster, sent title or downloaded media ([ADR 0011](adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md)).

## The apps it works with

| App | Read from it | Shared as | Found for it by |
| --- | --- | --- | --- |
| Sonarr | Series, episodes, episode files | Whole series, seasons, single episodes | TVDB, TMDb or IMDb ID, season and episode, scene numbering, air date for daily shows |
| [Radarr](https://radarr.video/) | Movies and their files | Whole movies | TMDb or IMDb ID |
| [Whisparr](https://whisparr.com/) v3 | Films and scenes, with studio, date, code and IDs | Whole items | Films by ID. Scenes by text: studio, date, title and code |
| Whisparr v2 | Sites and their scenes | Whole scenes | Scenes by studio, date, title or code; matches scenes shared by v2 or v3 |
| [Lidarr](https://lidarr.audio/) | Artists, albums and track files | Whole artists, or single albums. An album is offered only when complete | Text: artist and album |
| [Prowlarr](https://prowlarr.com/) | Nothing | Nothing | Nodearr adds itself there as an indexer ([ADR 0007](adr/0007-prowlarr-as-downstream-indexer-consumer.md)) |

Adult content is found only by searches in the XXX categories, and albums only in Audio. Nodearr registers Whisparr and Lidarr with those categories; Sonarr's and Radarr's normal categories leave them out. Filtering uses the request's categories, not the calling app's identity ([ADR 0015](adr/0015-the-indexer-id-matching-release-titles-and-candidate-ids.md), [ADR 0020](adr/0020-lidarr-albums-shared-and-moved-as-folders-of-tracks.md)).

## From start to finish

### 1. Adding an Arr app

Under **Settings > Apps** (or the first-run steps on **Home**), the person gives Nodearr the Arr app's address and API key, and the address at which the Arr app can reach Nodearr. Nodearr then registers itself inside the Arr app ([ADR 0010](adr/0010-arr-contract-newznab-indexer-and-sabnzbd-download-client.md)):

- a **download client**, as [SABnzbd](https://sabnzbd.org/), on the app's own category;
- an **indexer**, as Newznab, pinned to that download client, so nothing Nodearr finds is ever sent to another client;
- a **webhook**, so Nodearr hears within seconds when a file is imported, upgraded, renamed or deleted.

It then reads the app's library into its **local catalog**: every title, file, quality and quality profile. It reads it again every 12 hours, and a webhook has it read one title again at once. What the app stops reporting is marked missing, and removed after 7 days. See [catalog import](diagrams/catalog-import-sonarr-radarr.md).

Nodearr checks that the Arr app can see the folder Nodearr writes finished transfers to, through the app's own file browser, and says so on **System > Status** when it cannot ([ADR 0018](adr/0018-diagnostics-and-no-calls-home.md)).

### 2. Becoming friends

Each node has a key pair, kept by the sidecar. Its public key is the node's ID, and it never changes. A backup holds it, so a restored node is still the node its friends know ([ADR 0013](adr/0013-node-identity-pairing-and-trust.md)).

1. One person creates an **invite** on **Nodes > Invites** or on **Home**. It holds their node's ID, how to reach it, a random secret and an expiry, 24 hours by default. Only a hash of the secret is stored.
2. They send the code to their friend however they like.
3. The friend pastes it into their Nodearr, which connects to the first node. The connection's handshake proves each node holds the key for its ID. The friend's node sends the secret.
4. The first node checks it against an unused, unexpired invite, marks the invite used, and both nodes record each other as **trusted**.

Trust can be **revoked** from either side, which ends it on both, or the other node can be **blocked**, which turns its connections away before they reach Nodearr. A node that keeps failing to pair is turned away for 15 minutes. See [pairing](diagrams/peer-invite-trust-establishment.md).

### 3. Choosing what to share

A new friend is offered everything except **Hidden** from the apps marked **Share With New Friends**, unless the invite said otherwise, on either side. That setting starts on for every library app but Whisparr ([ADR 0014](adr/0014-sharing-rules-and-catalog-exchange.md), amendments). On **Catalog > Sharing**:

- a **share list** holds whole series, seasons, episodes, movies, Whisparr items, whole artists and single albums, and is given to chosen friends;
- a friend is marked **share everything**, or not, and which Arr apps that takes in;
- anything on the **Hidden** list is offered to nobody, whatever else says so.

A whole series, season or artist takes in what arrives later.

Something can also be **sent**: one title, or parts of it, to one friend, or to someone new with an invite made for it alone. It is shared with them on a list of their own, and their node is told, so their **Home** shows what arrived. **Get it** there adds it to their Arr app if needed, monitors what was sent and asks the app to search. The app chooses among its enabled indexers, including Nodearr ([ADR 0021](adr/0021-sending-a-title-to-a-friend.md)).

### 4. Seeing what friends have

Each node asks each friend for their **offer**: what that friend shares with this node, and nothing else ([ADR 0014](adr/0014-sharing-rules-and-catalog-exchange.md)). An offer says what each file is (its IDs, title, season and episodes, or album and tracks), how good it is (quality, resolution, codecs, size, release name) and its [BLAKE3](https://github.com/BLAKE3-team/BLAKE3) hash where the sender has worked it out. It says nothing of where the file is or what it is called, apart from its extension.

The offer is fetched every 15 minutes by default. A friend taking something back sends a hint to fetch it again, so it usually disappears within seconds. If the hint cannot be delivered, the next successful sync removes it; requests for the file are refused in the meantime. After the first time, only what changed is sent, 500 files to a page.

The peer catalog marks each file against the local library: **not in the library**, **missing**, an **upgrade** (one the Arr app's own quality profile would take in place of the one it has), or **have**. **Catalog > Peer Catalog** shows them, as a list or as a grid of posters. A poster comes from the friend's own Arr app over the same connection, and is kept once fetched ([ADR 0022](adr/0022-posters-from-friends-arr-apps.md)). See [catalog exchange](diagrams/catalog-exchange-trusted-nodes.md).

### 5. Searching

When an Arr app searches Nodearr, the answer comes from the peer catalogs, matched by the IDs the Arr app sends ([ADR 0015](adr/0015-the-indexer-id-matching-release-titles-and-candidate-ids.md)). Whisparr's scenes and Lidarr's albums have no IDs to send, and are matched by text.

Each result has a release title written the way the Arr app reads them: the release name the friend's Arr app kept where it has one, or one built from what the offer says. The Arr app judges quality and custom formats from that title itself, as it does with any indexer.

A result names no friend and no file. Its ID is a keyed hash only this node can produce or look up. The same file offered by two friends, by its hash, is one result. See [search, grab and import](diagrams/arr-search-grab-indexer-flow.md).

### 6. Grabbing and fetching

When the Arr app grabs a result, it gets a small NZB that is only a ticket naming the result. It hands that to Nodearr's download client as it would to SABnzbd, and Nodearr starts a **transfer** ([ADR 0016](adr/0016-transfers-and-the-download-client.md)):

1. The sidecar connects to the friend's node and asks for the file.
2. The friend's Nodearr checks the request at that moment: is this node still trusted, is the file still shared with it, and can it be read? Having been offered it earlier counts for nothing.
3. The friend's sidecar works out the file's size and BLAKE3 hash and sends them first, then the file.
4. This node writes it under `incomplete`, hashing as it goes, and compares the hash at the end.
5. Only a file that matches is moved, in one step, into `complete/<category>/<title>`, where the Arr app finds it, imports it, and tells Nodearr to remove it.

An album arrives as its tracks, one after another, and appears only once every one of them is verified ([ADR 0020](adr/0020-lidarr-albums-shared-and-moved-as-folders-of-tracks.md)).

Three transfers run at once. A file gets up to three attempts per source node, with 15 seconds between retries, carrying on from what had arrived. If another friend offers the same file with the same hash and size, the transfer can carry on from them instead. An album stays with its source node, and each track gets its own attempts. One that fails for good is reported to the Arr app with a reason written for its history, such as `NoDirectRoute` or `Revoked`, and the Arr app blocklists it and searches again. See [transfers](diagrams/direct-transfer-download-client-flow.md).

## How two nodes reach each other

Nodes learn each other's addresses from invites and pings. A node pings every trusted friend when its transport starts, when its network address has settled for three seconds after a change, and during its connection check every 15 minutes. Each ping sends the current IP addresses and home relay, and checks the route shown on **Nodes > Trusted Nodes** again. When a ping carries a changed address, the receiver also checks that friend's route and refreshes its open page. The periodic check retries friends that were offline. There is no address lookup service: two nodes that change networks together with relays off may lose track of each other ([ADR 0013](adr/0013-node-identity-pairing-and-trust.md#2026-10-29-tell-friends-when-the-network-address-changes-124)).

Most home machines are behind a router that does not let connections in. iroh gets two of them talking anyway ([ADR 0011](adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md)):

1. Each sidecar keeps a connection open to its relay, and learns its public address from it.
2. To reach a friend, it asks the relay to pass a message to the friend's node, then both try to reach each other at the addresses they learned. Both having sent first is what gets through most routers ("hole punching"). The sidecar can also ask the router for a port mapping (UPnP, NAT-PMP or PCP).
3. Pairing, connection checks and ending trust may go through the relay while this happens, encrypted end to end. The relay cannot read them. Catalogs, catalog changes, posters and titles sent to a friend, and their answers, wait for the direct path, up to the same 20 seconds, and fail as `NoDirectRoute` without it. The node that sends checks the path as it sends, and the node that receives refuses any that came through a relay ([ADR 0011, amendment of 2026-10-29](adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md#amendment-2026-10-29-what-may-cross-a-relay)).
4. The connection stays open while the two nodes are talking, and closes once nothing has been sent on it for 3 minutes. Browsing a friend's library is one connection, not one for each poster, so hole punching and the relay's part in it happen once ([ADR 0011, amendment of 2026-10-29](adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md#amendment-2026-10-29-control-connections-stay-open-while-nodes-are-talking)). Blocking a node closes everything open to it at once.
5. **File data moves only over a direct path.** The sender waits up to 20 seconds for one, and gives up with `NoDirectRoute` if none appears. If a direct path is lost part way, it stops with `DirectPathLost` rather than carry on through the relay.

This has worked between two home networks with no port forwarded, and between a home and a cloud machine. A node in [Docker](https://docs.docker.com/) Desktop on Windows or macOS is the known hard case: Docker Desktop gives it a different public port for every destination, so it needs the UDP port forwarded. See [when there is no direct path](diagrams/direct-connectivity-failure-no-relay.md).

## Privacy

- **What a friend sees** is the offer built for them: what is shared with them, described as above. Not what is hidden, not what is shared only with others, and not where anything is stored.
- **What the relay sees** is that two node IDs are talking, when, and the encrypted pairing and connection checks that pass through it before a direct path forms. Never a catalog, a poster or a file, so not how large a library is.
- **What is recorded today stays secret later.** Two updated nodes agree on keys with a post-quantum key exchange, X25519MLKEM768, which a future quantum computer cannot undo. With an older node they fall back to X25519 ([ADR 0023](adr/0023-post-quantum-key-exchange-between-nodes.md)).
- **What anyone else sees** is nothing. Nodearr makes no calls home: no update checks, no error reporting, no analytics. The code it was derived from did, and all of it was taken out ([ADR 0018](adr/0018-diagnostics-and-no-calls-home.md)).
- The web UI and the API ask for a sign-in or an API key, as Sonarr's do. That is separate from trust between nodes: an API key is not a node's identity.

## Where things are in the UI

| Page | What it is for |
| --- | --- |
| **Home** | What friends sent, the first-run steps, then an overview: what needs attention, what friends have, the library, friends and the network. |
| **Catalog > Local** | What the Arr apps say this node has. |
| **Catalog > Peer Catalog** | What friends share, against the local library. |
| **Catalog > Sharing** | Sending something to a friend, share lists, **Hidden**, and what each friend is offered. |
| **Nodes > Trusted Nodes** | Trusted, revoked and blocked nodes, and how each is reached. |
| **Nodes > Invites** | Creating invites and using a friend's. |
| **Activity > Transfers** | What is being fetched, and what failed and why. |
| **Settings > Apps** | The Arr apps, and Prowlarr. |
| **Settings > Node** | This node's name, the relay, the UDP port, port mapping, the download folder, and the upload and download limits ([ADR 0019](adr/0019-node-settings-saved-in-the-ui-fixed-by-the-environment.md)). |
| **Settings > General** | The web port, sign-in, the API key and backups. |
| System | Status and health, Tasks, Backup, Events and Log Files. |

## Packaging

One image for Docker, and packages for Linux, macOS and Windows like Sonarr's, including a Windows installer that runs Nodearr in the tray or as a service ([ADR 0017](adr/0017-one-docker-image-and-how-it-is-deployed.md), [releases](guides/releases.md)). Nodearr does not update itself.

## Further reading

- [Decision records](adr/), 0001 to 0022.
- [Sequence diagrams](diagrams/).
- [The V1 plan](plans/v1-implementation-plan.md): what was built, slice by slice, and what is still open.
- [What Nodearr inherited from Sonarr](research/inherited-sonarr-features.md).

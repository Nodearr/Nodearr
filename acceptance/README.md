# Acceptance tests

These scripts test complete Nodearr workflows through its HTTP APIs and real Arr apps. Run commands from the repository root. They start isolated test instances, so check that their ports are free first.

They need PowerShell 7 and, except for S8 which builds in [Docker](https://docs.docker.com/), a built Nodearr:

```sh
dotnet build src/Nodearr.sln
```

Each script starts from nothing, takes a few minutes, prints one line per check and exits non-zero if any check fails. Test data is stored under `acceptance/.run/`, which Git ignores.

The S3 script also needs Docker and ffmpeg. The S4 script needs neither, but needs the transport sidecar built:

```sh
cargo build --release --manifest-path transport/Cargo.toml
```

## S3: registration and catalog import

```sh
pwsh acceptance/Run-S3CatalogImport.ps1
```

Checks, against pinned [Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/) containers:

- Adding an application registers one Nodearr download client and one pinned Nodearr indexer in the Arr app, even while Nodearr's catalog is empty.
- Files the Arr apps import show up in the local catalog with their series or movie identity, quality and size, including one file that holds two episodes.
- Path mappings make the Arr apps' paths readable by Nodearr.
- A file deleted in Sonarr is marked missing on the next refresh and removed by a purge.
- Switching automatic fetching in Nodearr changes Sonarr's RSS switch in both directions. Syncing creates no duplicates, the scheduled jobs are the right ones, and deleting an application removes its catalog.
- Deleting an application also takes Nodearr out of that Arr app, as indexer and download client, and leaves the other app as it was. An application whose Arr app has been stopped can still be deleted, without a long wait.

Nodearr runs on the host on port 6633. The containers come from `docker-compose.arr.yml` with `docker-compose.library.yml` layered on top, which puts the Arr libraries on the host so Nodearr can read them. They publish on 127.0.0.1 only, on ports 18989 and 17878.

The Arr apps get their files from `Library.ps1`, which has them import synthetic ffmpeg test patterns (see [How the scripts fill a library](#how-the-scripts-fill-a-library)).

Options:

- `-KeepRunning` leaves Nodearr and the containers up, so you can open `http://localhost:6633/catalog/local`.
- `-Down` stops everything and deletes `.run/`.

## S4: identity, invites, trust and blocking

```sh
pwsh acceptance/Run-S4Pairing.ps1
```

Starts two Nodearr instances on this machine, A on port 6633 and B on port 6634, each with its own data folder and transport sidecar, and checks:

- Each node gets its own identity, kept in a key file.
- Redeeming an invite makes both nodes trust each other, and the invite works only once.
- A few seconds after pairing, each node has checked the connection to the other without being asked.
- A blocked node is refused when it connects, cannot pair again with a new invite, and cannot be deleted while blocked.
- Unblocking does not restore trust. Pairing again does, without duplicating the node.
- A restart keeps identity and trust, leaves no orphan sidecar, and the peer finds the node again.
- Revoking ends trust on both sides.

The sidecars run with relays and port mapping off and use local addresses, so nothing leaves the machine. Pass `-TransportBin` if the sidecar binary is not in `transport/target/release` or `$CARGO_TARGET_DIR/release`.

`-KeepRunning` leaves both instances up so you can open `http://localhost:6633/nodes/trusted` and `http://localhost:6634/nodes/invites`.

## S5: sharing and catalog exchange

```sh
pwsh acceptance/Run-S5Sharing.ps1
```

Requires PowerShell 7, a built Nodearr, Docker, ffmpeg and a built transport sidecar. Node A imports a real catalog from Sonarr and Radarr containers. Node B imports from the same Sonarr only. The two pair, and the script reads what B holds after each change on A:

- A peer paired with sharing everything unticked is offered nothing.
- "Share everything" offers every file once B syncs. New sharing is not announced; taking a file back is.
- B marks each file as have, missing or not in library against its own catalog.
- Nothing B receives says where a file is stored.
- Hiding a series removes it. A share list offers only its titles, and only to the peers on it.
- Revoking trust empties B's copy, and B cannot fetch it again.

`-KeepRunning` leaves both instances and the containers up. Open `http://localhost:6633/catalog/sharing` on A and `http://localhost:6634/catalog/peers` on B.

## S6: the indexer

```sh
pwsh acceptance/Run-S6Indexer.ps1
```

Requires PowerShell 7, a built Nodearr, Docker, ffmpeg and a built transport sidecar. It starts four containers: node A's Sonarr and Radarr, which hold the files, and node B's Sonarr and Radarr, which know the same series and movie and hold nothing. The nodes pair, A shares, and the script searches from B's Sonarr and Radarr and reads what they make of Nodearr's answers:

- Sonarr and Radarr find A's files by ID, read the right episode and quality from the title, and approve them.
- A file with no release name gets a title built from what A's Sonarr knows about it.
- A result keeps its ID and publish date when the catalog is fetched again, and names neither the node nor its file.
- The link in a result returns a ticket only for an ID Nodearr issued.
- Hiding a series or revoking trust on A empties the searches on B.
- A grab hands the ticket to Nodearr, which starts a transfer.

`-KeepRunning` leaves everything up. B's Sonarr is at `http://localhost:28989` and B's Radarr at `http://localhost:27878`. Open a Pingu episode in Sonarr and use the interactive search to see the results as a person would.

## How the scripts fill a library

`Library.ps1` copies a test video into the folder an Arr app has mounted at `/downloads` and tells the app to import it, the way a finished download is imported. The name of the file decides what the app takes it to be.

## S7: transfers

```sh
pwsh acceptance/Run-S7Transfers.ps1
```

Requires PowerShell 7, a built Nodearr, Docker, ffmpeg and a built transport sidecar. Takes about five minutes. Node A's uploads are slowed so that the script can act in the middle of a transfer. It presses "search" in node B's Sonarr and Radarr and follows each grab:

- The file comes from node A, is verified, and is imported. The imported file is the same, byte for byte.
- With RSS enabled in Sonarr, pairing grabs nothing from the initial library. An episode added afterwards is fetched by **RSS Sync** and imported without a search.
- A new app starts with automatic fetching on and upgrades off; switching automatic fetching off and on in Nodearr changes RSS in a real Sonarr.
- Nothing appears under `complete` while a file is still arriving.
- The same ticket twice gives one transfer. Deleting it from the queue leaves nothing behind.
- A download limit on node B, a quarter of A's upload limit, holds a transfer to it.
- A's Sonarr takes a PROPER of an episode B holds. B is offered it as a PROPER, shown it as an upgrade, and its Sonarr grabs it from A and imports it in place of its copy.
- Node B is killed in the middle and started again. The transfer carries on from what had arrived.
- Node A revokes trust in the middle. The transfer stops, and Sonarr records why and blocklists the release.
- B's Sonarr is switched to call the series daily, while A's still calls it standard. It then asks by the day the episode aired, is answered with a title written by day, and imports the file.
- Node A, the one sending, shows each upload while it leaves and lists how each ended: sent, stopped by the other side, or cut when trust ended.

### Trying it by hand

If the consumer ports are in use, pass `-SonarrConsumerPort` and `-RadarrConsumerPort` to S7 to use different ports. Their defaults are 28989 and 27878.

```sh
pwsh acceptance/Run-S7Transfers.ps1 -SetupOnly
```

This builds the same environment, pairs the nodes, shares everything, and stops there with nothing grabbed. `acceptance/.run/s7/ready.txt` appears when it is ready, with the addresses. Then:

1. Open node B's Sonarr at `http://localhost:28989`, go to Pingu, and search for one of the first three episodes of season 1. Use the interactive search to see Nodearr's result before grabbing it.
2. Watch it arrive at `http://localhost:6634/activity/transfers`, under **Receiving**. The same page on node A, `http://localhost:6633/activity/transfers`, shows it leaving, under **Sending**.
3. Sonarr imports it within a minute. The episode then shows as downloaded.

Node B's Radarr at `http://localhost:27878` has one movie to try the same with. Node A is at `http://localhost:6633`: **Catalog > Sharing** decides what B is offered, and **Nodes > Trusted Nodes** can revoke B.

Add `-UploadLimitKbps 300` to make transfers slow enough to watch. To stop everything and remove the containers:

```sh
pwsh acceptance/Run-S7Transfers.ps1 -Down
```

## Get it from the peer catalog

```sh
pwsh acceptance/Run-CatalogGetIt.ps1
```

Reuses S7's two nodes, Sonarr and Radarr, and adds two pinned Lidarr 3.1 containers on ports 18686 and 28686. It requests a movie, one episode, a season and an album through the catalog Get it API, starting with no titles in the receiving apps. Each ends imported, with SHA-256 matching the source bytes. It also checks a PROPER upgrade, monitoring only the requested parts, and no received-send cards on Home. The album is six silent FLAC files tagged as the six-track CD release of Slint's *Spiderland*.

The test resets only `acceptance/.run/s7` and volumes belonging to `nodearr-s7`. Ask before deleting existing test data. `-KeepRunning` leaves it up; `-SkipSetup` reuses `Run-S7Transfers.ps1 -SetupOnly` for debugging and still removes B's test series and movie. Pass `-TransportBin` when the sidecar is elsewhere. Results are in `.run/s7/getit-results.json`. Like S7, this checks only a direct route on one machine.

## Identical copies

```sh
pwsh acceptance/Run-IdenticalCopies.ps1
```

Requires PowerShell 7, a built Nodearr, Docker, ffmpeg and a built transport sidecar. Takes about four minutes. Three nodes on this machine: A and C read the same Sonarr library, so they hold the very same episode file, and B has its own Sonarr and trusts both. It checks:

- A and C offer the file with the same hash, and B holds both copies.
- B's Sonarr is shown the file once, not once for each node.
- B grabs it. The node it comes from is stopped part way. The transfer carries on from the other node from where it was, not from the start, and Sonarr imports the right file with no failure in its history.

`-KeepRunning` leaves everything up, with A on port 6633, B on 6634 and C on 6635. `-Down` stops it all.

## S8: the Docker image, deployed twice

```sh
pwsh acceptance/Run-S8Docker.ps1
```

Needs PowerShell 7, Docker and ffmpeg, with no [.NET](https://dotnet.microsoft.com/), Node or [Rust](https://www.rust-lang.org/) on the host. The first run builds the Nodearr image and a small relay image used only by this test, which takes ten minutes or so. Later runs take about five. Add `-SkipBuild` to reuse the images.

It runs `acceptance/docker-compose.s8.yml`: two complete deployments, John's and Ryan's, each a Nodearr container with its own Sonarr and Radarr, one shared `/downloads` volume and its libraries mounted read-only. The two sides reach each other over one network that stands in for a direct route, and a relay that stands in for a public one.

- The container runs as the chosen user, keeps the key private, can write to `/downloads` and cannot write to the libraries.
- No path mappings are needed anywhere.
- A grab in Ryan's Sonarr and one in his Radarr are fetched from John's node and imported, byte for byte.
- Ryan's Nodearr container is replaced in the middle of a transfer. The new one is the same node and carries on.
- John hides a series while Ryan's node is frozen and cannot hear of it. Ryan's node then uses a ticket it already held, and John's node refuses.
- One byte of a file is changed on John's side after its hash was fixed, its size and time kept. The first attempt fails verification, John's node forgets the hash, and the next attempt brings the changed file to Sonarr.
- The direct network is unplugged. The transfer fails as `NoDirectRoute`, and the relay's counters show that no file data went through it. While only the relay is left, **System > Status** warns that there is no direct route, names the other node and says to check the firewall. The warning goes once the route is direct again. The failed transfer has the connection checked again unasked, and what Sonarr records says that the check found only a relay and where to look.
- John blocks Ryan in the middle of a transfer. It is cut.
- A connection check says "direct" between the two, and "relay" once the direct network is unplugged.
- A third Nodearr, set up wrongly on purpose, is warned that it cannot read the library and that Sonarr cannot see its downloads. The two set up properly show no warnings.
- That third node logs every request it makes for the whole run. All of them are to the one Sonarr it was given.

`-KeepRunning` leaves it all up: John's Nodearr at `http://localhost:6633`, Ryan's at `http://localhost:6634` and the wrongly set up one at `http://localhost:6635`. By then John has blocked Ryan. `-Down` removes everything.

## Node settings

```sh
pwsh acceptance/Run-NodeSettings.ps1
```

Requires PowerShell 7, a built Nodearr and a built transport sidecar. Docker and ffmpeg are not needed. Takes about two minutes and starts two local nodes. It changes one node's settings through the API, the way the page does, and checks what the running node then does:

- Bad values are refused together, each named, and nothing is changed. A port another program holds is refused.
- A setting fixed in the environment stays as it is.
- A new name reaches a trusted node. A new port, upload and download limits and wait restart the transport, which comes back as the same node, and the trusted node reaches it at the new port without pairing again.
- A new download folder is created and is what Sonarr and Radarr are then told.
- Everything survives a restart of Nodearr.

`-KeepRunning` leaves both up. Open `http://localhost:6633/settings/node`.

## General settings

```sh
pwsh acceptance/Run-GeneralSettings.ps1
```

Requires PowerShell 7, a built Nodearr, Docker and a built transport sidecar. Takes about three minutes. One real node on this machine, and a Sonarr container for the one check that needs an Arr app. Unlike the other scripts the node is not given its port, key or way of signing in through the environment, because those are what is being changed. It checks, through the API the way the page does:

- The page shows where Nodearr listens, its key, how signing in works, and which settings the environment fixes.
- Settings that cannot work are refused, each named, and nothing is changed. A setting the environment fixes stays as it is.
- A new log level is used straight away.
- A new port waits for a restart. After a restart from the page Nodearr answers on it and nowhere else, as the same process and the same node with one sidecar. A second restart works as well.
- Signing in by form can be turned on, and a changed password is the only one that works afterwards.
- Resetting the API key replaces it at once, with no restart, and Sonarr is given the new one: its own tests of the Nodearr indexer and download client pass.

`-KeepRunning` leaves it up at `http://localhost:6633/settings/general`. `-Down` stops it and deletes `.run/general`.

## Between two machines

Use [Testing between two machines](../docs/guides/two-machine-test.md) for Docker or existing Arr installations. For two Windows machines without Arr apps installed, use [the Windows test guide](../docs/guides/two-machine-test-windows.md). The acceptance scripts on this page test only one machine.

## What the scripts do not cover

These were checked by hand against real apps, each recorded where it was decided, and have no script here:

- [Prowlarr](https://prowlarr.com/): adding Nodearr to Prowlarr and switching its copies off ([ADR 0007](../docs/adr/0007-prowlarr-as-downstream-indexer-consumer.md#amendments)).
- [Whisparr v3 and v2](https://whisparr.com/): films and scenes read, shared, found by text and imported, including a v2 and a v3 finding each other's scenes ([ADR 0015](../docs/adr/0015-the-indexer-id-matching-release-titles-and-candidate-ids.md#amendments)).
- **The Windows installer, tray app and service** ([release guide](../docs/guides/releases.md#on-windows-the-installer)).
- **The pairing limit**: covered by the unit tests only ([ADR 0013](../docs/adr/0013-node-identity-pairing-and-trust.md#amendments)).

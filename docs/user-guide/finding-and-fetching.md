# Finding and fetching

## What your friends have

**Catalog > Peer Catalog** lists every file your friends share with you, each marked against your own library:

![Catalog > Peer Catalog: Sam's files, with posters, each marked Not in library or Have](images/peer-catalog.png)

| Status | Meaning |
| --- | --- |
| **Missing** | The series, movie, scene or album is in one of your Arr apps, and you have no file for it. |
| **Upgrade** | You have a file, and your friend's is one your Arr app's quality profile would take in its place: better, with your copy still below the profile's cutoff and upgrades allowed. |
| **Have** | You have a file, and this offer is not an upgrade under your local quality profile. This also applies when upgrades are off or the cutoff is met. |
| **Not in library** | None of your Arr apps has it. Add it there first if you want it. |

Select **Posters** to switch from the file list to a grid. The grid groups files into one tile per series, movie, scene or album, including copies shared by different friends. Each tile shows how many files you have or are missing. Select **Table** to return to the file list.

![The Posters view of Sam's catalog: one tile each for Cowboy Bebop, Frieren and Spirited Away, with what Alex has of each](images/peer-catalog-posters.png)

Posters come from your friends' Arr apps. Nodearr caches each image for 30 days before requesting it again; it does not contact an external image service ([ADR 0022](../adr/0022-posters-from-friends-arr-apps.md)).

Filter by friend or select a status to show only matching files. To refresh all friends' offers now, select **Sync Peer Catalogs**. Scheduled refreshes run every 15 minutes by default; change **Catalog Sync Interval** under **Settings > Node** if needed.

**Home** shows how many files your friends have that you are missing or could upgrade.

## How your Arr apps find it

After you [connect an Arr app](connecting-apps.md), it searches Nodearr alongside its other indexers. Nodearr returns matching files from your friends' catalogs. The Arr app decides whether to download a result using its own quality profiles and other rules.

- Interactive searches show Nodearr's results alongside other results so you can choose one.
- Automatic searches can select a Nodearr result when it is the best acceptable match.
- Newly shared files do not trigger downloads by default. Nodearr registers its indexer with RSS disabled. Search in the Arr app to discover those files.

To enable RSS searches, first set the app's **Sync Level** to **Add Only** in Nodearr under **Settings > Apps**. Then edit the **Nodearr** indexer in the Arr app under **Settings > Indexers** and turn on **Enable RSS**. With **Full Sync**, Nodearr restores RSS to its disabled default at the next sync.

Results use the release name saved by the sender's Arr app, or a title Nodearr builds from the catalog metadata. The receiving app evaluates quality and custom formats from that title as it does for any indexer.

The same file offered by two friends is one result. A result says nothing about which friend it is from.

### What each app is shown

- [Sonarr](https://sonarr.tv/) is shown episodes, matched by the series' IDs and the season and episode it asks for. A file holding several episodes is a result only for those episodes.
- [Radarr](https://radarr.video/)'s default movie categories return movies matched by TMDb or IMDb ID and exclude [Whisparr](https://whisparr.com/)'s films.
- Whisparr is shown films and scenes, from friends' Whisparr v2 and v3. These need a search in the XXX categories.
- [Lidarr](https://lidarr.audio/) is shown albums, by artist and album. These need a search in the Audio categories.

The categories in the request decide what can be found, not which app sent it. Sonarr's and Radarr's default categories leave adult content and albums out. A manual search in [Prowlarr](https://prowlarr.com/) can find them when those categories are selected.

## What happens to a grab

When an Arr app grabs a Nodearr result, it sends the request to Nodearr's download client using the [SABnzbd](https://sabnzbd.org/) interface. In Nodearr, **Activity > Transfers** shows the download and any files you are uploading to friends.

![Activity > Transfers: three episodes of Frieren coming from Sam, part way, and Cowboy Bebop sent to Sam earlier](images/transfers.png)

1. **Queued**: waiting for a slot. Three transfers run at once.
2. **Downloading**: arriving from your friend, with its progress. Your Arr app's queue shows the same.
3. **Completed**: it arrived, matched the hash your friend's node gave before sending, and was moved into place in one step. Your Arr app imports it at its next check, usually within a minute, and then asks Nodearr to remove it.
4. **Failed**: the transfer stopped without a verified file. **Activity > Transfers** and the Arr app's history show the reason. See [When a transfer fails](#when-a-transfer-fails).

Nodearr writes incoming files under `incomplete` in its download folder. After verification, it moves them into `complete` for the Arr app to import. An album becomes available for import only after every track has arrived and passed verification.

If Nodearr restarts, it resumes unfinished transfers. If the sender goes offline, Nodearr retries while attempts remain. Once the attempts are exhausted, wait for the friend to return, then search again in your Arr app or select **Retry** in Nodearr.

A retry resumes from the downloaded portion if the file has not changed. Nodearr can switch to another friend offering an identical file, identified by its hash and size. Album transfers stay with their original sender.

## When a transfer fails

A transfer gets up to three attempts per source, with 15 seconds between retries. Each track in an album has its own three attempts. If the transfer fails after those attempts, Nodearr reports the failure to the Arr app, which records the reason, blocklists the result and searches again.

| Reason | What it means | What to do |
| --- | --- | --- |
| `NoDirectRoute` | The two nodes could only reach each other through a relay, and files never go that way. | See [Relay only](troubleshooting.md#relay-only). |
| `DirectPathLost` | The direct connection was lost during the transfer. | Let Nodearr retry. If failures continue, see [Relay only](troubleshooting.md#relay-only). |
| `ConnectionLost` | The connection to the other node was lost. | Let Nodearr retry. If attempts are exhausted, check that the friend is online before trying again. |
| `PeerUnreachable` | The other node could not be reached. It may be offline. | Wait until it is back, then search again. |
| `NotAuthorized` | The other node no longer shares this file with you. | Nothing: they chose to stop. |
| `Revoked` | Trust between the two nodes has ended. | Pair again, if you both want to. |
| `Blocked` | The other node has blocked yours. | Transfers cannot resume unless they unblock your node and you pair again. |
| `NotFound` | The other node no longer has the file, or cannot read it. | They may need a [path mapping](connecting-apps.md#when-nodearr-sees-the-library-at-another-path). |
| `VerificationFailed` | The downloaded file did not match its expected hash. Nodearr discards it and retries from the start. | Let Nodearr retry with a newly calculated hash. If failures continue, ask the sender to check the source file. |
| `DiskError` | The file could not be written here. | Check the download folder has room and can be written to. **System > Status** says if it cannot. |

On **Activity > Transfers**, select **Retry** to create a new attempt for a failed transfer. **Remove** deletes the transfer and its downloaded files, including any partial download.

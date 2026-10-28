# Finding and fetching

## What your friends have

**Catalog > Peer Catalog** lists every file your friends share with you, each marked against your own library:

![Catalog > Peer Catalog: Sam's files, with posters, each marked Not in library or Have](images/peer-catalog.png)

| Status | Meaning |
| --- | --- |
| **Missing** | The series, movie, scene or album is in one of your Arr apps, and you have no file for it. |
| **Upgrade** | You have a file, and your friend's is one your Arr app's quality profile would take in its place: better, with your copy still below the profile's cutoff and upgrades allowed. A PROPER or REPACK of the quality you have counts too, unless your Arr app's **Propers and Repacks** setting says not to upgrade to them. The quality then has a **Proper** or **Repack** label beside it. |
| **Have** | You have a file, and this offer is not an upgrade under your local quality profile. This also applies when upgrades are off or the cutoff is met. |
| **Not in library** | None of your Arr apps has it. **Get it** can add it for you. |

Both **Table** and **Posters** have the same kind filters: **Series**, **Movies**, **Music** when Lidarr is connected, and **Scenes** when Whisparr is connected. The selection is remembered on this page. In **Table**, a show opens into seasons, then episode files. A file with several episodes appears once under its season. Show and season rows count distinct episodes across friends, missing or upgradable episodes and total offered size; their status is **Missing** if any episode is missing, otherwise **Upgrade** if any is upgradable. The tree uses Sonarr's TVDB season and episode numbers. Anime rows show absolute numbers, scene numbers appear alongside TVDB numbers when they differ, daily shows show air dates, and specials (season 0) appear last.

Select **Posters** to switch from the file list to a grid for the selected kind. The grid groups files into one tile per title, including copies shared by different friends. Each tile shows how many files you have or are missing. Select **Table** to return to the file list.

Select a series, episode, movie, Whisparr film or scene poster or title to open its details card. For a file with several episodes, choose the episode in the card. The details are what that friend shared from their Arr app, including the resolution they reported; your Nodearr does not connect to the friend's Arr app. The arrow beside a series or season expands its rows, and **Get it** remains a separate action. Lidarr details cards are planned separately.

![The Series Posters view of Sam's catalog: tiles for Cowboy Bebop and Frieren](images/peer-catalog-posters.png)

Choose **Movies** to see movie posters on their own:

![The Movies Posters view of Sam's catalog: Spirited Away](images/peer-catalog-movies-posters.png)

Posters come from your friends' Arr apps. Nodearr caches each image for 30 days before requesting it again; it does not contact an external image service ([ADR 0022](../adr/0022-posters-from-friends-arr-apps.md)).

Filter by friend or select a status to show only matching files. To refresh all friends' offers now, select **Sync Peer Catalogs**. Scheduled refreshes run every 15 minutes by default; change **Catalog Sync Interval** under **Settings > Node** if needed.

**Home** shows how many files your friends have that you are missing or could upgrade.

## Get it from the catalog

Select **Get it** beside a file or under a poster. For TV, choose **Episode file**, **Season** or **Whole series** in the dialog. An episode file can contain several episodes; all of them are selected together. Whole series follows Sonarr's All Episodes choice and leaves specials out; choose season 0 or an episode file for those. For music, the selection is one complete album.

![Get it from a friend's catalog, with the episode file and Sonarr's choices](images/peer-catalog-get-it.png)

Nodearr fills in the root folder and quality profile most of your library uses. A new series starts with your friend's series type; you can change it. Lidarr also needs a metadata profile. If you have several apps of the right kind, choose which app to use. A title already in that app keeps its settings.

Select **Get it** in the dialog to add the title, monitor what you chose and search. Progress appears there; after the app grabs a result, follow it on **Activity > Transfers**. You can close the dialog and reopen it while the app is being asked. No send card is added to Home.

TV goes to Sonarr, movies to Radarr, music to Lidarr, and adult films and scenes to Whisparr v3. A scene known only by Whisparr v2's own ID cannot be added this way; the dialog explains how to add it in Whisparr instead. If an app does not answer or has no root folder, fix that in the app and return to the dialog. Valid choices stay selected when the plan refreshes.

Only trusted friends' current offers can be requested. **Have** has no action. **Upgrade** can be requested, and your Arr app decides whether to replace your file using its own profiles and rules. Searching uses all the app's enabled indexers, so it may choose another acceptable release.

## Try your first transfer

Both nodes must be running, and your friend must share at least one file you want.

1. In Nodearr, go to **Nodes > Trusted Nodes** and select **Check connection** for your friend. **Direct** means files can move. **Relay only** allows messages but no media transfers; if it stays that way, see [Relay only](troubleshooting.md#relay-only).
2. Open **Catalog > Peer Catalog** and select **Sync Peer Catalogs**. Find a file your friend shares. If their catalog is empty, ask them to check their app choices and **Hidden** on **Catalog > Sharing**.
3. Select **Get it** beside the file or under its poster, check the choices, then select **Get it** in the dialog. You can also add it in your Arr app and run an interactive search there. If the app rejects a result, read its reason; your quality profile or other rules may exclude it.
4. In Nodearr, open **Activity > Transfers** and wait for **Completed**. The file has then passed verification. Your Arr app imports it at its next download-client check, usually within a minute, then removes the completed download from Nodearr.
5. Check the title in your Arr app to confirm that the file was imported. If the transfer completed but import failed, check the app's queue and Nodearr's **System > Status** for a folder-access warning.

> [!NOTE]
> Pairing does not immediately download the friend's existing library. New app connections fetch later missing arrivals automatically by default; search for older files or use **Get it**.

## How your Arr apps find it

After you [connect an Arr app](connecting-apps.md), it searches Nodearr alongside its other indexers. Nodearr returns matching files from your friends' catalogs. The Arr app decides whether to download a result using its own quality profiles and other rules.

- Interactive searches show Nodearr's results alongside other results so you can choose one.
- Automatic searches can select a Nodearr result when it is the best acceptable match.
- New connections automatically fetch later arrivals the app monitors and is missing. Existing connections keep their previous RSS choice after upgrading.

In Nodearr, go to **Settings > Apps**, edit the app and use **Fetch new files from friends automatically** to control RSS. **Including upgrades** starts off; enable it to let the app replace existing files when its profile allows an upgrade. Select **Save**. Nodearr applies these choices at each registration sync, including with **Add Only**.

Each connected library app has its own RSS feed. It lists files that arrived in a friend's shared catalog after the initial sync and that this app is missing, plus upgrades only when **Including upgrades** is on. A file you already have at that quality, or a title outside that app's library, is left out. Pairing does not announce the friend's existing library through RSS; you can still find those files by searching.

You can exclude a friend from every app's automatic feed under **Catalog > Sharing**. See [Choosing which friends to fetch from automatically](friends-and-sharing.md#choosing-which-friends-to-fetch-from-automatically).

RSS skips friends whose last connection check found no direct route, because their files cannot be downloaded. Friends not yet checked remain eligible. A later catalog update that only changes an existing file's description does not announce it as a new arrival.

Existing indexers receive their app-specific address at the next registration sync. The legacy address used by Prowlarr and registrations not yet updated keeps its unfiltered feed. Prowlarr's copies remain disabled.

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

The number beside **Activity** counts downloads that are queued, downloading, waiting to retry, or failed and still need attention. Completed downloads and uploads do not count. The badge disappears at zero and turns red while any failed download remains. It updates as transfers change.

![Activity sidebar badge showing one unfinished download while a Frieren episode comes from Sam, with completed Cowboy Bebop uploads below](images/transfers.png)

- **Queued**: waiting for a slot. Three transfers run at once.
- **Downloading**: arriving from your friend, with its progress. Your Arr app's queue shows the same.
- **Completed**: it arrived, matched the hash your friend's node gave before sending, and was moved into place in one step. Your Arr app imports it at its next check, usually within a minute, and then asks Nodearr to remove it.
- **Failed**: the transfer stopped without a verified file. **Activity > Transfers** and the Arr app's history show the reason. See [When a transfer fails](#when-a-transfer-fails).

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

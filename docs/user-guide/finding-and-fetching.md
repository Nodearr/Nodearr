# Finding and fetching

## What your friends have

**Catalog > Peer Catalog** lists every file your friends share with you, each marked against your own library:

| Status | Meaning |
| --- | --- |
| **Missing** | The series, movie, scene or album is in one of your Arr apps, and you have no file for it. |
| **Upgrade** | You have a file, and your friend's is one your Arr app's quality profile would take in its place: better, with your copy still below the profile's cutoff and upgrades allowed. |
| **Have** | You have a file at least as good. |
| **Not in library** | None of your Arr apps has it. Add it there first if you want it. |

Filter by friend, or by status with the buttons along the top, which count each one. **Sync Peer Catalogs** fetches every friend's offer now, rather than at the next sync (every 15 minutes unless you change it under Settings > Node).

Home shows how many files your friends have that you are missing or could upgrade.

## How your Arr apps find it

Your Arr apps find what friends have the way they find anything: by searching their indexers, and Nodearr is one of them. A result from Nodearr comes from a friend's offer, never from anywhere else.

- **When you search by hand**, the app's interactive search lists Nodearr's results with the others. The result's title is the release name your friend's Arr app kept, or one Nodearr writes from what the offer says, so the app can judge its quality and custom formats as it judges any other.
- **When the app searches by itself**, as it does for something just added, or with Search All Missing and Search Monitored, it grabs a result from Nodearr if it is the best it finds.
- **What a friend adds later is not grabbed by itself.** Nodearr's indexer is added with RSS off, so the app does not keep polling it for anything new. Search for the item, or the missing items, to pick it up. If you want the app to grab what friends add without being asked, turn on **Enable RSS** for the Nodearr indexer in the app's Settings > Indexers, and set that app's Sync Level in Nodearr to **Add Only**. With Full Sync, Nodearr puts its indexer back as it set it, RSS off, at the next sync.

The same file offered by two friends is one result. A result says nothing about which friend it is from.

### What each app is shown

- **Sonarr** is shown episodes, matched by the series' IDs and the season and episode it asks for. A file holding several episodes is a result only for those episodes.
- **Radarr** is shown movies, by TMDb or IMDb ID. Never Whisparr's films.
- **Whisparr** is shown films and scenes, from friends' Whisparr v2 and v3. Only Whisparr's searches, in the XXX categories, find them.
- **Lidarr** is shown albums, by artist and album. Only Lidarr's searches, in the Audio categories, find them.

## What happens to a grab

The app sends what it grabbed to Nodearr's download client, as it would to SABnzbd, and **Activity > Transfers** shows it as Nodearr fetches it. The same page lists what your node is sending to friends.

1. **Queued**: waiting for a slot. Three transfers run at once.
2. **Downloading**: arriving from your friend, with its progress. Your Arr app's queue shows the same.
3. **Completed**: it arrived, matched the hash your friend's node gave before sending, and was moved into place in one step. Your Arr app imports it at its next check, usually within a minute, and then asks Nodearr to remove it.
4. **Failed**: it did not arrive. The reason is in Activity > Transfers and in the Arr app's history. See below.

Nothing half arrived is ever where your Arr app looks: a transfer is written under `incomplete` and only moved to `complete` once checked. An album appears only once every one of its tracks has arrived and been checked.

If Nodearr or its friend stops part way, the transfer carries on from what had arrived when both are back, as long as the file has not changed. If the friend goes away and another friend has the same file, by its hash, it carries on from them.

## When a transfer fails

A transfer that fails for a reason that may pass, such as a lost connection, is tried again, up to three times, 15 seconds apart, from where it got to. One that fails for good is reported to the Arr app, which records the reason in its history, blocklists that result, and searches again.

| Reason | What it means | What to do |
| --- | --- | --- |
| `NoDirectRoute` | The two nodes could only reach each other through a relay, and files never go that way. | See [Relay only](troubleshooting.md#relay-only). |
| `DirectPathLost` | The direct connection was lost part way. | Usually passes. If it keeps happening, see [Relay only](troubleshooting.md#relay-only). |
| `ConnectionLost` | The connection to the other node was lost. | Usually passes. |
| `PeerUnreachable` | The other node could not be reached. It may be offline. | Wait until it is back, then search again. |
| `NotAuthorized` | The other node no longer shares this file with you. | Nothing: they chose to stop. |
| `Revoked` | Trust between the two nodes has ended. | Pair again, if you both want to. |
| `Blocked` | The other node has blocked yours. | |
| `NotFound` | The other node no longer has the file, or cannot read it. | They may need a [path mapping](connecting-apps.md#when-nodearr-sees-the-library-at-another-path). |
| `VerificationFailed` | What arrived did not match its hash, so it was thrown away. Tried again from the start. | Usually passes: the other node works the hash out again. |
| `DiskError` | The file could not be written here. | Check the download folder has room and can be written to. System > Status says if it cannot. |

**Retry** on Activity > Transfers tries a failed transfer again, as a new one. **Remove** deletes a transfer and whatever had arrived of it.

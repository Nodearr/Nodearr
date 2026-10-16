# Connecting your apps

Nodearr works through the Arr apps you already have. It reads each one's library, and adds itself to each as an indexer and a download client, so what your friends have turns up in your searches and comes in like any other download.

Add each app under **Settings > Apps**, with the **+** button, or from the first step on Home.

## What to fill in

| Field | What it is |
| --- | --- |
| Name | What Nodearr calls the app. Anything. |
| Sync Level | **Full Sync** (recommended): Nodearr keeps what it added to the app as it set it, and puts back any change made there. **Add Only**: it adds what is missing and leaves your changes alone. **Disabled**: Nodearr neither reads the app nor keeps anything in it. |
| Nodearr Server | The address **the app** uses to reach Nodearr. The default is right when both run on the same machine, outside Docker. |
| Application Server | The address **Nodearr** uses to reach the app. |
| API Key | The app's own key, from its Settings > General. |
| Username, Password (advanced) | Only when the app sits behind a reverse proxy that asks for HTTP Basic sign-in. |
| Download Category (advanced) | The category the app sends its downloads to Nodearr under. |
| Sync Categories (advanced) | The categories Nodearr's indexer is added to the app with. The app only searches it in these. |

**Test** checks that Nodearr can reach the app, that the API key works, and that the app is the one you chose, in a version Nodearr supports. **Save** then adds Nodearr to the app and reads its library, which takes from seconds to a few minutes for a large one.

### The defaults for each app

| App | Address | Category | Searched in | Needs at least |
| --- | --- | --- | --- | --- |
| Sonarr | `http://localhost:8989` | `tv` | 5030, 5040, 5045, and 5070 for anime | 3.0.5 |
| Radarr | `http://localhost:7878` | `movies` | 2030, 2040, 2045 | 3.0 |
| Whisparr v3 | `http://localhost:6969` | `whisparr` | 6000, 6040, 6045, 6080, 6090 | 3.0 |
| Whisparr v2 | `http://localhost:6969` | `whisparr` | 6000, 6040, 6045, 6080, 6090 | 2.0, below 3.0 |
| Lidarr | `http://localhost:8686` | `music` | 3000, 3010, 3040 | 2.0 |
| Prowlarr | `http://localhost:9696` | | | |

Both versions of Whisparr call themselves Whisparr. Add the one you run: a Whisparr v3 added as v2 is told to be added as v3.

## When the apps are in Docker

"localhost" inside a container is the container itself, so the two addresses depend on where each side runs:

| Nodearr | The app | Nodearr Server | Application Server |
| --- | --- | --- | --- |
| On the machine | On the machine | `http://localhost:6633` | `http://localhost:8989` |
| On the machine, or in Docker with the host's network | In Docker on a bridge | `http://host.docker.internal:6633` (the container needs `extra_hosts: host.docker.internal:host-gateway` on Linux) | `http://localhost:8989` |
| In Docker on the same bridge network | In Docker on that network | `http://nodearr:6633` | `http://sonarr:8989` |

Use the service names from your compose file, and the app's own port.

## What Nodearr adds to each app

All three are named **Nodearr**, so you can see them in the app:

- **A download client** (Settings > Download Clients), as SABnzbd, on the app's category, set to remove downloads once imported.
- **An indexer** (Settings > Indexers), as Newznab, tied to that download client so nothing found through Nodearr is ever sent to another client. RSS is off: friends' new files are found by the app's own searches, and Nodearr does not flood the app's RSS sync. Automatic and interactive search are on.
- **A connection** (Settings > Connect), as a webhook, so Nodearr hears within seconds when the app imports, upgrades, renames or deletes something.

Deleting the app in Nodearr takes all three out of it, and deletes what Nodearr read from it.

## The two folders that have to line up

### Where transfers go

The app has to see the folder Nodearr writes finished transfers to, at the path it expects, or it never imports them. Nodearr checks this itself, through the app's own file browser, and System > Status says so when it cannot see it.

- In Docker, mount one folder at the same path, such as `/downloads`, in Nodearr and in every app.
- Outside Docker, apps on the same machine see Nodearr's folder as it is. The default is a `downloads` folder in Nodearr's data folder. Set another under Settings > Node.
- If the app sees the folder at another path, as an app on another machine does, add a **remote path mapping in the app** (its Settings > Download Clients), from Nodearr's path to its own.

### When Nodearr sees the library at another path

To send a file to a friend, Nodearr reads it from the path the app reports. If Nodearr sees the library at another path, such as when the app is in Docker and Nodearr is not, add a **path mapping in Nodearr**, at the bottom of Settings > Apps:

| Host | Remote path | Local path |
| --- | --- | --- |
| The app's host, as in its Application Server address | The path the app reports, such as `/tv/` | Where Nodearr finds it, such as `D:\Media\TV\` |

Catalog > Local shows whether each file can be read, and System > Status warns when files cannot be. A friend is offered such files, and then refused them, until it is fixed.

## Each app

### Sonarr

Nodearr reads every series, episode and episode file. Sonarr searches Nodearr by the IDs it knows: TVDB, TMDb and IMDb, and the season and episode. A series that the scene numbers differently is searched by the scene's numbers, and a daily show by the day it aired. Both work. Anime is searched in category 5070, by the same IDs and numbers.

You can share a whole series, a season or single episodes.

### Radarr

Nodearr reads every movie and its file. Radarr searches by TMDb and IMDb ID.

### Whisparr v3 (Eros)

Nodearr reads films and scenes, with each one's studio, release date, code and IDs. Whisparr searches for a film by its TMDb ID, and for a scene by text: its studio, date, title or code, in the forms Whisparr uses. Nodearr answers both.

Adult content is only ever found by searches in the XXX categories, which only Whisparr makes. Radarr never sees it, even for a film it also knows.

### Whisparr v2

Nodearr reads each site and its scenes, and keeps each scene as Whisparr v3 keeps one. A v2 and a v3 therefore find each other's scenes, and a scene one has is shown as held by the other.

Whisparr v2 tells Nodearr which site changed, not which scene, so Nodearr reads the whole library again after each change. On a very large library that takes a little while.

### Lidarr

Nodearr reads artists, albums and track files. You share whole artists. An album is offered only when Lidarr holds every track of it, and it is moved as one folder of tracks, which appears for Lidarr only once every track has arrived and been checked.

Lidarr searches by artist and album, as text, in the Audio categories. Albums are only ever found by those searches. An album's release title is the name Lidarr kept for the release, or `Artist - Album (Year) [Quality]`.

Lidarr does not rename tracks by default, and then keeps the names they arrive with: the release title and a track number, such as `Slint - Spiderland (1991) [FLAC] - 01.flac`, straight in the artist's folder. Turn on Rename Tracks in Lidarr's Settings > Media Management for names of your own choosing.

### Prowlarr

Prowlarr holds no library, so Nodearr reads nothing from it. Adding it makes Nodearr one of Prowlarr's indexers, called Nodearr, so you can search it there with your others.

Prowlarr copies every indexer into each app it manages. Those copies are not tied to Nodearr's download client, so a grab through one would go to the wrong client and fail. Nodearr therefore gives its indexer in Prowlarr a sync profile of its own, also called Nodearr, with RSS and both kinds of search off: the copies arrive switched off. **Add each Arr app to Nodearr directly as well**, as above; it is that indexer they use.

## Reading the library again

Nodearr reads each app's whole library every 12 hours, and one title again within seconds whenever the app's webhook says it changed. Catalog > Local can read everything again at once with **Refresh Catalog**. What an app stops reporting is marked missing, and removed after 7 days, or at once with **Purge Missing**.

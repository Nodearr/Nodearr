# Privacy and security

Nodearr separates trust between friends from access to its web UI and API. This page explains what information leaves your node and how to protect it.

## What a friend sees

A friend's catalog contains only files you share with them. Each offer includes metadata their Arr apps use to identify and evaluate the file:

- Identity: title, year, provider IDs, season and episode details, studio and release date, or artist, album and track count.
- Release information: quality, resolution, codecs, audio channels, languages, custom formats, release group, size and any saved release name.
- File information: import time, extension such as `.mkv`, and hash.

They do not see where any file is stored, what it is called on your disk, what you hide, what you share only with others, what else is in your library, or who your other friends are.

Friends also receive your node's network addresses, including its public IP address and local network addresses, and its relay address. Invite codes contain these details too. Share invites only with people you trust with that information.

## What a friend can do

- Request a file you currently share with them. Nodearr checks trust and sharing permissions on every request. An old catalog entry does not grant access after you stop sharing the file.
- Peer trust gives no access to your folders, web UI, API or Arr apps. Those have separate access controls. Nodes talk to each other's transport: pairing, checking in, exchanging catalogs and removal notices, sending a title's note or poster, revoking trust, and requesting a shared file.

Revoking or blocking a friend ends all of it at once, including anything being sent to them.

## What the relay sees

By default, Nodearr uses public relays operated by [n0](https://n0.computer), the makers of [iroh](https://www.iroh.computer/). A relay can see which node IDs communicate, their IP addresses and when they connect. It carries what two nodes need to find a path to each other: pairing, connection checks, ending trust, and the request and header that start a transfer, all encrypted, so it cannot read them. What your library holds does not cross it: catalogs, posters, titles sent to a friend and downloaded media travel only directly between the two nodes. A friend reached only through a relay can pair with you, but cannot browse your library.

To use your own relay, follow [iroh's relay setup guide](https://docs.iroh.computer/add-a-relay). Then select **A relay of your own** under **Settings > Node** and enter its **Relay Address**. You can also select **No relay** if both nodes have addresses the other can reach directly. The direct-only rule for media applies with any relay.

## What anyone else sees

Nodearr contacts the Nodearr project in two ways, each every 6 hours and each with its own setting ([ADR 0024](../adr/0024-update-checks-and-usage-reports-on-nodearr-com.md)): it checks `updates.nodearr.com` for a new version, and it sends an anonymous usage report to `usage.nodearr.com`. It sends no error reports, news requests or time-service requests, and the web interface has no tracking script. Those inherited calls were removed ([ADR 0018](../adr/0018-diagnostics-and-no-calls-home.md)).

To stop both, turn off **Contact nodearr.com** under **Settings > General** ([Settings](settings.md#updates)). With it off, Nodearr contacts only the Arr apps you connect, the friends you trust and the relay you choose.

### Update checks

- **Two requests for files, and nothing else:** `GET https://updates.nodearr.com/v1/main.json` and `GET https://updates.nodearr.com/v1/main.json.sig`. They carry no identifier, no query string and no cookies. The `User-Agent` header says `Nodearr` and the first two numbers of the version, such as `Nodearr/0.10`.
- **Who sees them:** the host of `updates.nodearr.com`, which is [GitHub Pages](https://pages.github.com/). Like any web server it sees the address the requests come from, so it can tell that an address runs Nodearr. Nothing in the requests links one check to the next. The Nodearr project receives no logs of them.
- **What it gets back:** a list of the newest release's packages, signed by the maintainer. Nodearr checks the signature with a key built into it, and uses nothing from a list that isn't signed.
- **When it installs an update:** it downloads the package for your platform from GitHub Releases, with the same `User-Agent` and no cookies.

To stop the check alone, turn off **Check for Updates**.

### Anonymous usage report

Like Sonarr's and Radarr's **Send Anonymous Usage Data**, this is on by default. It tells the maintainer roughly how many nodes run, on which platforms, versions and Arr apps, and how often transfers fail and why, so that testing and fixes go where they matter. The totals are public, at [nodearr.com/stats](https://nodearr.com/stats).

- **One request, and nothing else:** `POST https://usage.nodearr.com/v1/report` every 6 hours, with the report as JSON. No query string and no cookies, and the same `User-Agent` as the update check. A report that can't be sent is dropped, and the next one is sent 6 hours later.
- **What it never holds:** titles, paths or file names, node IDs, the names or IDs of friends, IP addresses, host names, user names, API keys, Arr app addresses, or exact counts. Every count is a range, such as `2-5`.
- **No identifier that lasts:** each report carries a random ID that is replaced at midnight UTC. It lets the totals count each node once a day. Nothing links one day's reports to the next.
- **See it before it goes:** **Show Report** under **Settings > General** shows the exact report the next run would send. The report sent is also written to the debug log.
- **What is kept, and for how long:** `usage.nodearr.com` runs on [Cloudflare Workers](https://workers.cloudflare.com/). It refuses a report with any field not in its [published schema](https://github.com/Nodearr/Nodearr/blob/main/usage/schema/v1/report.schema.json). It never reads or stores the address a report came from, keeps no request logs, and stores each report with only the UTC day it arrived, keeping the latest one for each ID. Reports are kept 90 days, then only each day's totals. In the published totals, any count under 10 is shown as "fewer than 10". The service's code is [in the Nodearr repository](https://github.com/Nodearr/Nodearr/tree/main/usage). Like any host, Cloudflare sees the address a request comes from while it answers it.

To stop the report alone, turn off **Send Anonymous Usage Data**.

Every field the report holds:

| Field | What it is |
| --- | --- |
| `schema` | The version of the report's format: `1`. |
| `id` | The random ID for the day. |
| `nodearr.version`, `nodearr.branch` | The Nodearr version, such as `0.11.0`, and `main`, or `other` for a branch you named yourself. |
| `nodearr.platform`, `nodearr.runtime` | The platform the package is for, such as `win-x64` or `linux-musl-arm64`, and the .NET version. |
| `nodearr.database` | `sqlite` or `postgres`. |
| `nodearr.install` | How Nodearr was installed: `installerTray`, `installerService`, `zip`, `tarball`, `docker`, `package` or `development`. |
| `active` | Whether this node moved a file or answered a grab in the past 30 days. |
| `apps` | Each Arr app connected: its kind, such as `sonarr`, and its major and minor version, such as `4.0`. Not its address or name. |
| `friends` | How many trusted friends, as a range: `0`, `1`, `2-5`, `6-10` or `11+`. |
| `sharedTitles` | For each kind of Arr app, how many titles are shared with at least one friend, as a range. |
| `past7Days.sent`, `past7Days.fetched` | Transfers finished and failed in the past 7 days, the data moved, and failures by reason, such as `NoDirectRoute`. All ranges. |
| `past7Days.sends` | How many titles were sent to a friend with **Send**, as a range. |
| `network.friendsDirect`, `network.friendsRelayOnly`, `network.friendsUnreachable` | How many friends the connection checks of the past 7 days reached directly, only through a relay, or not at all. Ranges. |
| `network.portVaries`, `network.udp` | Whether this node's network gives a different public port for each destination, and whether UDP gets out. |
| `network.relay` | Which relay is set: `n0` for the default, `own` or `off`. Not its address. |
| `network.dockerDesktop` | Whether Nodearr runs in a container on Docker Desktop. |
| `features` | Which features are on: share lists, the Hidden list, sharing everything with a friend, fetching new files automatically, the upload and download limits, and whether **Most Uploads at Once** is the default, another number or no limit. |
| `language` | The language of the web interface, such as `en`. |

An example report:

```json
{
  "schema": 1,
  "id": "3b2f7c1e-9a4d-4f6b-8e2a-5c7d1f0b9e34",
  "nodearr": {
    "version": "0.11.0",
    "branch": "main",
    "platform": "linux-musl-x64",
    "runtime": "10.0.1",
    "database": "sqlite",
    "install": "docker"
  },
  "active": true,
  "apps": [
    {
      "kind": "radarr",
      "version": "5.28"
    },
    {
      "kind": "sonarr",
      "version": "4.0"
    }
  ],
  "friends": "2-5",
  "sharedTitles": {
    "radarr": "101-1000",
    "sonarr": "11-100"
  },
  "past7Days": {
    "sent": {
      "finished": "6-25",
      "failed": "1-5",
      "data": "10-100GB",
      "failures": {
        "NoDirectRoute": "1-5"
      }
    },
    "fetched": {
      "finished": "1-5",
      "failed": "0",
      "data": "1-10GB",
      "failures": {}
    },
    "sends": "0"
  },
  "network": {
    "friendsDirect": "2-5",
    "friendsRelayOnly": "1",
    "friendsUnreachable": "0",
    "portVaries": false,
    "udp": true,
    "relay": "n0",
    "dockerDesktop": false
  },
  "features": {
    "shareLists": false,
    "hiddenTitles": true,
    "shareEverything": true,
    "fetchAutomatically": true,
    "uploadLimit": false,
    "totalUploadLimit": false,
    "downloadLimit": false,
    "maxUploads": "default"
  },
  "language": "en"
}
```

This doesn't hide your network traffic: friends and relay operators can see the connection metadata described above.

Your router also receives port-mapping requests unless you turn off **Ask the Router to Open the Port** under **Settings > Node**.

## Encryption and checking

- Every connection between nodes is encrypted (QUIC, TLS 1.3), and each side proves it holds the key for its node ID during the handshake. A node cannot pretend to be another without that node's key.
- Two nodes on a release newer than 0.6.0 also use a post-quantum key exchange (X25519MLKEM768). Someone who records their traffic today cannot decrypt it later with a quantum computer. This covers messages and files that pass through a relay, too. With a friend on 0.6.0 or earlier, the connection uses X25519, as before, until they update ([ADR 0023](../adr/0023-post-quantum-key-exchange-between-nodes.md)).
- Every file is checked against the [BLAKE3](https://github.com/BLAKE3-team/BLAKE3) hash its sender gave before sending. A file that does not match is thrown away and never reaches your Arr app.

## Keeping your node safe

- **The node's key** is stored in `node.key` in the data folder. Backups include it if it exists when the backup is made. Anyone with the key can impersonate your node to its friends. Keep the data folder and backups private.
- **Invite codes** work once, for 24 hours unless you choose otherwise. Until one is used, whoever holds it can pair with you. Send it only to the person it is for.
- **Sign-in.** Nodearr asks for a password on the first start. Keep it on, and do not make the web UI reachable from the internet. If you need to reach it from outside, put it behind a reverse proxy with HTTPS and its own sign-in, as you would [Sonarr](https://sonarr.tv/).
- **The API key** lets whoever has it do anything the web UI can. Your Arr apps are given it. Reset it under **Settings > General** if it leaks.
- **Your Arr apps' API keys** are kept in Nodearr's database, as [Prowlarr](https://prowlarr.com/) keeps them. Anyone who can read the data folder can read them.
- **Strangers trying to pair** are turned away after five failed attempts in 15 minutes, and **System > Status** warns you. An invite's secret is 256 bits and cannot be guessed.

## What you share

Nodearr moves files only between people who chose to trust each other, and only what each chose to share. What you share, and whether you may, is up to you.

## Reporting a security problem

See [SECURITY.md](../../SECURITY.md).

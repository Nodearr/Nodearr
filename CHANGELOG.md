# Changelog

What changed in each release, newest first. Each item links to the decision record that explains it, and the [V1 plan](docs/plans/v1-implementation-plan.md) has the whole story, slice by slice.

## Unreleased

### Fixed

- One invite could pair two nodes, when both used it at the same moment: each could find it unused before either marked it redeemed. Redeeming it is now a single step in the database, so only one of them pairs ([#31](https://github.com/Nodearr/Nodearr/issues/31)).
- Two downloads with the same release title went to the same folder, and finishing the second deleted the first before its Arr app had imported it. Like SABnzbd, Nodearr now puts the second next to the first, as `Title.1`, `Title.2` and so on, and tells the Arr app which folder it is in ([#32](https://github.com/Nodearr/Nodearr/issues/32)).
- A friend's node could make yours fetch and keep a catalog of any size, by claiming up to 10,000 pages and filling each one. Nodearr now takes at most 200,000 files from a friend, three times the largest library measured, and refuses a catalog that says more or a page that holds more. What it had from that friend stays. A real catalog over 10,000 pages was also cut short without a word, and taken for the whole ([#33](https://github.com/Nodearr/Nodearr/issues/33)).
- Preparing shared files for friends stored each file's hash with its own database write. On a library of 66,000 files that took 2 minutes 25 seconds before any file was read. The hashes are now stored in batches, at least every 5 seconds, and the same work takes under 3 seconds ([#34](https://github.com/Nodearr/Nodearr/issues/34)).

## 0.2.6 (pre-release)

### Security

- **With sign-in turned off for local addresses or localhost, a device on the network could get past it** by sending an `X-Forwarded-For` header that claimed a local address: Nodearr believed that header from every private network. Sign-in on, the default, was not affected. Nodearr now believes it only from a proxy on the same machine and from the networks in the new **Trusted Networks** setting, empty by default, and never skips sign-in for a forwarded address it did not believe, as Sonarr has since done ([GHSA-w69m-gw3g-623m](https://github.com/Nodearr/Nodearr/security/advisories/GHSA-w69m-gw3g-623m)).

### Fixed

- A title put on **Hidden** while a friend was part way through fetching your catalog could still reach them in the catalog's later pages. Any change to sharing now makes a friend part way through start again, from what is shared now. Files were never sent: each file request is checked against sharing as it is.

## 0.2.5 (pre-release)

### Documentation

- Added a complete [Getting started guide](https://nodearr.com/getting-started/) with downloads, installation, app connections, pairing and checks for the first transfer on one page.
- Reorganized the website around setup, everyday use, reference and releases. The homepage's main action is **Get started**; packages and downloads remain available on their own page.
- Grouped platform, app, networking and invite alternatives into tabs. Procedures use numbered steps, important notes use callouts, and optional instructions use expandable sections. The shared guide remains readable on GitHub.
- Expanded installation and connection instructions with inline Docker examples, folder access and permissions, app addresses, and host or bridge networking. Windows startup choices and both invite roles include the context each reader needs.
- Matched the website's header, homepage title and API header to the repository's two-color Nodearr wordmark in light and dark themes.

### Fixed

- The website's API reference returned 404 at `/api/` during local development. It now opens at `/api` and `/api/` in development and production builds.

## 0.2.4 (pre-release)

### Documentation

- Reworked the user guide into clearer procedures with explicit prerequisites, app locations and expected results. Whisparr v2 and v3 instructions now include the context each version needs.
- Applied Google's documentation guidance: bold UI labels, links to third-party projects, consistent terminology and simpler help text. Contributor and test guides now make shared steps and dependencies explicit.
- The guide's screenshots show the new help texts. A root folder's free space in them is an example, as the folders and addresses already were.

## 0.2.3 (pre-release)

### Fixed

- Sonarr, and the other Arr apps, warned that Nodearr's download folder for their category did not exist, such as `/downloads/complete/tv`, until the first transfer for it arrived. Nodearr now makes each category's folder when the app reads its download client settings.
- The settings link beside a System > Status warning about reading a library or the download folder led to a page that does not exist; it opens Settings > Apps. Links for checks inherited from Sonarr, whose pages Nodearr does not have, are gone.
- System > Status said Nodearr could not read files from an app when the files were not there, such as after a tool transcoded them to a new name and the app was not told. It said nothing when files were there and Nodearr was not allowed to open them. Nodearr now opens the files it checks, and the warning says which it found: no files at all, files it may not open, or files the app lists that are not on disk, with what to do for each. Catalog > Local's Readable column, which also only meant a file was there, now opens each file shown and says which.
- The link the API gives with each warning pointed to Servarr's wiki, which has no page for Nodearr. It points to the troubleshooting guide on nodearr.com, at the section for that warning.

## 0.2.2 (pre-release)

### Security

- **Restoring a crafted backup could write files outside the restore folder**, anywhere the account Nodearr runs as can write. A restore needs a signed-in user or the API key; nothing a friend's node sends can cause one. An entry that would land outside the folder is now refused ([GHSA-gfvh-m4vj-ggh3](https://github.com/Nodearr/Nodearr/security/advisories/GHSA-gfvh-m4vj-ggh3)).

### Fixed

- A restore accepted a `.xml` file and put it where the database is restored from, which would leave the next start without a database. A restore now takes a `.zip` backup, or a database file ending in `.db`.

### Changed

- `yarn typecheck` checks the web UI's TypeScript on its own, as `yarn build` does, and CI runs it.

### Documentation

- Corrected the 0.1.0 descriptions below: adult content and albums are selected by search categories, not the calling app's identity; Get it asks the Arr app to search its enabled indexers and may fetch another indexer's release; restoring a node's identity needs a backup containing `node.key`.
- The guide and UI help now explain sharing by app, best-effort removal notices, three transfer attempts per source, poster cache expiry, password recovery and platform data folders. Build and test instructions match the scripts they describe.

## 0.2.1 (pre-release)

### Changed

- **The user guide has screenshots**, of two test nodes, taken by `tools/docs-screenshots.mjs` so they can be taken again when the pages change.
- **Issues on GitHub** open as a bug report or a feature request form, which asks for what a Nodearr problem needs. Security problems are pointed to private reporting.

### Fixed

- A page that does not exist said "DefaultNotFoundMessage" instead of a sentence. Any page drawn once, before the words had arrived, showed their keys.
- A series whose title already ends with its year, as TVDB names some, showed the year twice: "Bluey (2018) (2018)".
- On Activity > Transfers, a transfer's percentage was cut off at the edge of the part already done, so early on it could not be read.

## 0.2.0 (pre-release)

### New

- **Share everything, from the apps you choose.** Under each friend on Catalog > Sharing, a tick for each Arr app says which libraries "everything except Hidden" takes in, so you can share everything but Whisparr. Lists still share from any app, and Hidden still wins. **Share With New Friends**, on each app in Settings > Apps, says which apps a new friend gets: all but Whisparr unless you change it. Friends you have keep what they had ([ADR 0014](docs/adr/0014-sharing-rules-and-catalog-exchange.md), amendments).

### Changed

- **A new icon.** Two arcs round a node: yours on one side, a friend's on the other, and the node between them shared, half each. It is in the web UI, the browser tab, the Windows program, tray and installer, and the README. Debug builds show the dark version.

## 0.1.0 (pre-release)

The first release: everything V1 set out to do.

- **Sonarr and Radarr**: their libraries read, kept current by a webhook, and Nodearr registered in each as a pinned Newznab indexer and a SABnzbd download client ([ADR 0010](docs/adr/0010-arr-contract-newznab-indexer-and-sabnzbd-download-client.md)).
- **Lidarr.** Artists, or single albums of them, are shared, whole albums are offered and compared by release group, and an album is moved as a folder of tracks, named as Lidarr names its own, that appears only once every track is checked. Lidarr finds albums by artist and album ([ADR 0020](docs/adr/0020-lidarr-albums-shared-and-moved-as-folders-of-tracks.md)).
- **Whisparr v3 (Eros) and Whisparr v2.** Films are found by ID and scenes by Whisparr's own text search: studio, date, title and code. A v2 and a v3 find each other's scenes. Adult content is only ever found by Whisparr's searches, never by Radarr's ([ADR 0015](docs/adr/0015-the-indexer-id-matching-release-titles-and-candidate-ids.md), amendments).
- **Prowlarr.** Nodearr adds itself to Prowlarr as an indexer, to be searched there with your others, and switches off the copies Prowlarr would make in your apps ([ADR 0007](docs/adr/0007-prowlarr-as-downstream-indexer-consumer.md), amendments).
- **Friends**: one-time invites, trust, revoking and blocking, over iroh, with each node's key as its identity. A node that keeps failing to pair is turned away for 15 minutes ([ADR 0013](docs/adr/0013-node-identity-pairing-and-trust.md)).
- **Sharing**: a new friend is offered your whole library except the Hidden list, unless you untick that and choose what they see, by series, season, episode, movie, artist or album. Offers are built for each friend and sent as changes after the first time. An upgrade is what your Arr app's own quality profile would take ([ADR 0014](docs/adr/0014-sharing-rules-and-catalog-exchange.md)).
- **Send to a friend.** Pick a title, or some seasons, episodes or albums of it, and a friend, or someone new, who is sent an invite made for it. **Get it** on their Home adds it to their Sonarr, Radarr, Lidarr or Whisparr v3 with choices filled in the way most of their library is, and searches, so the app fetches it from you ([ADR 0021](docs/adr/0021-sending-a-title-to-a-friend.md)).
- **Posters** in a friend's catalog, and a grid to browse it by, from the friend's own Arr apps. No image service is contacted ([ADR 0022](docs/adr/0022-posters-from-friends-arr-apps.md)).
- **Searching**: by ID, scene numbering and air date, with release titles the Arr apps read correctly. The same file from several friends is one result ([ADR 0015](docs/adr/0015-the-indexer-id-matching-release-titles-and-candidate-ids.md)).
- **Transfers**: direct only, never through a relay, checked by BLAKE3 hash, resumed after a stop, and carried on from another friend with the same file ([ADR 0011](docs/adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md), [ADR 0016](docs/adr/0016-transfers-and-the-download-client.md)).
- **A first run that sets itself up**: a welcome, sign-in set up first, and three steps on Home, which then becomes an overview.
- **Diagnostics**: whether a friend is reachable directly, and warnings for a library Nodearr cannot read or a download folder an Arr app cannot see. No calls home, and none of the error reporting inherited from Sonarr ([ADR 0018](docs/adr/0018-diagnostics-and-no-calls-home.md)).
- **Settings > Node and Settings > General**, with environment variables that fix a setting in place ([ADR 0019](docs/adr/0019-node-settings-saved-in-the-ui-fixed-by-the-environment.md)), and **System** pages for Tasks, Backup, Events and Log Files. A backup holds the node's key, so a restored node is the one its friends know.
- **Packages** for Linux, macOS and Windows, a Windows installer that starts Nodearr in the tray or as a service, and an image for amd64 and arm64, each with third-party notices for everything it contains ([ADR 0017](docs/adr/0017-one-docker-image-and-how-it-is-deployed.md), [releases](docs/guides/releases.md)).
- **A user guide** in `docs/user-guide/`, `SECURITY.md`, and an API description generated from the code.
- Run between two home networks with no port forwarded.

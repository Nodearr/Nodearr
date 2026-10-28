# Changelog

What changed in each release, newest first. Each item links to the decision record that explains it, and the [V1 plan](docs/plans/v1-implementation-plan.md) has the whole story, slice by slice.

## 0.5.0 (pre-release)

Your Arr apps now fetch new files from friends by themselves. When a friend gets something you are missing, it arrives without anyone searching, and limits keep a node with many friends from being swamped ([#71](https://github.com/Nodearr/Nodearr/issues/71)).

### Added

- **Fetch new files from friends automatically** and **Including upgrades** on each library app in **Settings > Apps**. Nodearr now sets the app's RSS switch for its indexer from them at every sync. A new app fetches automatically, with upgrades off, so it only takes what it is missing. An existing app keeps the RSS choice you had made in the Arr app. Prowlarr is unchanged ([#74](https://github.com/Nodearr/Nodearr/issues/74)).
- Choose whether to automatically fetch new files from each friend under **Catalog > Sharing**, on by default. Searches and **Get it** still find their files ([#76](https://github.com/Nodearr/Nodearr/issues/76)).
- A **Total Upload Limit** on **Settings > Node**, beside the upload limit: the most all uploads to friends may send together, shared evenly between them, 0 for none, or `Nodearr__Transport__TotalUploadLimitKbps` in the environment. Each upload still keeps to the per-upload limit ([#84](https://github.com/Nodearr/Nodearr/issues/84), part of [#75](https://github.com/Nodearr/Nodearr/issues/75)).
- **Most Downloads at Once** on **Settings > Node**: how many downloads from friends run at the same time, 3 by default as before, 0 for no limit, or `Nodearr__Transport__MaxDownloads` in the environment. It applies at once. Grabs beyond it wait in order, shown as **Queued** in **Activity** and in the Arr app's queue ([#86](https://github.com/Nodearr/Nodearr/issues/86), part of [#75](https://github.com/Nodearr/Nodearr/issues/75)).
- **Most Uploads at Once** on **Settings > Node**: how many files you send to friends at the same time, 3 by default, 0 for no limit, or `Nodearr__Transport__MaxUploads` in the environment. Further requests wait their turn instead of being refused, so the friend's Arr app does not blocklist the release. The friend's **Activity** shows **Waiting** with its place in line, and its Arr app shows the download as queued. A friend on an older version waits its turn too, without being told, so the limit holds for everyone ([#85](https://github.com/Nodearr/Nodearr/issues/85), part of [#75](https://github.com/Nodearr/Nodearr/issues/75)).

### Changed

- Each library app has its own RSS feed from Nodearr, and it lists only what that app would want: files that arrived after pairing, that the app is missing (or could upgrade, when **Including upgrades** is on), from friends Nodearr can reach directly or has not checked yet. Pairing with a friend no longer offers their whole library to RSS, and friends reachable only through a relay are left out, since files never travel that way. Searches, and the shared feed Prowlarr uses, are unchanged. The apps are moved to their own feed at the next sync ([#73](https://github.com/Nodearr/Nodearr/issues/73)).
- **Uploads to friends are now three at a time by default**, where there was no limit before. Requests beyond that wait their turn instead of being refused. Set **Most Uploads at Once** to 0 to go back to no limit ([#85](https://github.com/Nodearr/Nodearr/issues/85)).
- Nodes on this release can make each other wait for an upload slot. A node on an earlier release still sends and receives as before. It is never told it is waiting, but it waits its turn all the same ([#85](https://github.com/Nodearr/Nodearr/issues/85), ADR 0016).

### Fixed

- A long title under a poster in either catalog was cut off mid-letter. It now ends in an ellipsis, and the year is on its own line below, so it is never the part cut off. The full title shows on hover.

### Documentation

- The user guide's screenshots were retaken with public-domain titles only: Night of the Living Dead, The Little Shop of Horrors, Nosferatu, Metropolis, Steamboat Willie and Aladdin and His Wonderful Lamp, with the first Out of the Inkwell and Felix the Cat shorts as the series. The UI test kit (`tools/ui-test.ps1`) now uses these titles. The screenshots show public-domain posters from Wikimedia Commons instead of the ones Sonarr and Radarr fetch, which are often modern artwork; `tools/screenshot-posters` lists where each came from ([UI test guide](docs/guides/ui-test.md)).

## 0.4.2 (pre-release)

### Added

- Local series cards mark episodes in the library and missing episodes. Turn off **Show missing episodes** in a series card or the local series table to see only downloaded files. Local series, movie and readable episode cards offer **Send to a friend**, with the title or episode selected in the send form.
- Friend series and episode cards show whether you have each offered episode, and movie cards show your library status. The series card can hide episodes you have not downloaded.

### Changed

- The light theme uses light teal navigation and toolbar surfaces, with text and controls suited to the brighter palette.

### Fixed

- Opening a card keeps the page in place, so you can return to where you were browsing.

## 0.4.1 (pre-release)

### Added

- Series details cards in both catalogs list the episodes by season, each linking to its own details card ([#77](https://github.com/Nodearr/Nodearr/pull/77)).

### Changed

- The light theme uses Nodearr's colors, and on poster tiles the **Send** button sits over the poster ([#77](https://github.com/Nodearr/Nodearr/pull/77)).

### Fixed

- On phones, a page could stop scrolling after a dialog was closed. Closing a dialog now always releases the scroll lock ([#77](https://github.com/Nodearr/Nodearr/pull/77)).
- An Arr app's RSS setting for the Nodearr indexer stays as you set it when Nodearr syncs its registration. New indexers still start with RSS off ([#72](https://github.com/Nodearr/Nodearr/issues/72)).

### Documentation

- Contributors can run the CI test suites with `npm test`, or run frontend checks, builds and those tests with `npm run verify`. The local and CI runs share the same test selection ([contributing guide](CONTRIBUTING.md#testing)).

## 0.4.0 (pre-release)

### Added

- **Get it** in **Catalog > Peer Catalog**, beside files and under posters. Choose an episode file, season, whole series, movie, scene or album. Nodearr fills in the app choices, adds or monitors the selection, and asks the Arr app to search. Progress appears in the dialog and downloads appear in **Activity > Transfers**; the Arr app still decides which release to fetch ([#54](https://github.com/Nodearr/Nodearr/issues/54)).
- **Send** from **Catalog > Local**. Table rows select their movie, episode or episodes, album, series or season; poster tiles select the whole title. Both open the send form, and the friend receives the same Home card as a send from **Catalog > Sharing** ([#59](https://github.com/Nodearr/Nodearr/issues/59)).
- Separate **Series**, **Movies**, **Music** and **Scenes** filters in both catalog views. Music appears when Lidarr is connected, and Scenes when Whisparr is connected. TV tables expand into series, seasons and episode files, with episode counts and sizes; the peer catalog also summarizes missing and upgradable episodes. Rows show anime absolute numbers, differing scene numbers and daily air dates; specials appear last ([#58](https://github.com/Nodearr/Nodearr/issues/58)).
- Details cards for Sonarr series and episodes, Radarr movies, and Whisparr films and scenes. Select a poster or title in either catalog to see its description and other Arr information, including file resolution and all available resolutions for a series. A friend's card uses the details shared through Nodearr. Lidarr details cards are deferred ([#64](https://github.com/Nodearr/Nodearr/issues/64), [#68](https://github.com/Nodearr/Nodearr/issues/68)).
- An **Activity** sidebar badge counts unfinished downloads, including those waiting to retry. Unresolved failures count and turn the badge red; completed downloads and uploads do not count ([#60](https://github.com/Nodearr/Nodearr/issues/60)).
- PROPER, REPACK and REAL revisions are read from the Arr app and included in shared release titles. Both catalogs show revision labels beside the quality. A later revision counts as an upgrade according to the local Arr app's Propers and Repacks setting ([#51](https://github.com/Nodearr/Nodearr/issues/51)).

### Fixed

- **Get it** on Home refreshes empty or removed root folders and profiles when the Arr app's choices change, while keeping valid choices. Invalid choices are refused before adding is queued. Successful cards disappear once the local catalog holds what was sent, or 24 hours after success; failed cards stay until retried or put away ([#53](https://github.com/Nodearr/Nodearr/issues/53)).
- Overlapping catalog syncs could store a friend's offers more than once. Syncs for one friend now run in order, and revoking or deleting that friend waits for its sync before clearing the catalog ([#54](https://github.com/Nodearr/Nodearr/issues/54)).
- A brief loss of the selected direct path could interrupt an in-flight transfer. Nodearr now keeps the pending write and continues if a direct route returns within **Wait for a Direct Route**. Selecting a relay still ends the transfer before media can use it. Logs now record path changes, connection closure reasons, and connection-check results and round-trip times ([#55](https://github.com/Nodearr/Nodearr/issues/55)).
- Automatic upload retries stay in one sender row, retaining the previous failure while retrying and showing the attempt count when complete. Separate downloads remain separate rows ([#56](https://github.com/Nodearr/Nodearr/issues/56)).

## 0.3.0 (pre-release)

### Added

- Posters everywhere a title is shown, not only in a friend's catalog: **Catalog > Local** shows each file's poster and has a **Posters** view, one tile per series or movie with how many files it has; the **Sharing** page's lists and the Send form show a small poster beside each series and movie; and a card on **Home** for what a friend sent shows the title's poster. Your own titles' posters come from your own Arr apps, and nothing new goes between nodes ([#42](https://github.com/Nodearr/Nodearr/issues/42)).
- A download limit on **Settings > Node**, beside the upload limit: the most each download from a friend may take, 0 for none, or `Nodearr__Transport__DownloadLimitKbps` in the environment. A relay of your own is asked before a new address is saved, and one where no relay answers is refused with what happened, instead of being found out when the transport starts ([#48](https://github.com/Nodearr/Nodearr/issues/48)).

### Changed

- **Anything that calls Nodearr's `/api/v3`, or a v5 endpoint for Sonarr's own library, now gets a 404.** Use `/api/v5`; the API reference lists what it has. The rest of what came from Sonarr and nothing in Nodearr uses is gone: its v3 API, a deprecated copy beside Nodearr's v5, and in v5 the series, episode, calendar, wanted, season pass, manual import, import list, connection, indexer, download client, queue, history, blocklist, release, parse, profile, quality, custom format, custom filter, auto tagging, metadata, root folder, disk space, language and tag detail endpoints, Sonarr's file type, media file, UI language and route table lookups, and the UI, naming, media management, indexer, import list and update settings. A crawl of every page and dialog found Nodearr's UI calling none of them, and the Arr apps and Prowlarr call only Nodearr's own: its Newznab feed, its SABnzbd endpoint and its webhook. `GET /api` lists no deprecated versions. Nothing contacts `services.nodearr.com` or the Nodearr-named sign-ins at `auth.servarr.com` any more: the proxy health check checks that the proxy's name resolves instead of pinging that host through it, scene mappings no longer come from it, and the AniList, MyAnimeList, Simkl and Trakt import lists and the Trakt connection are removed, with any saved ones. The Arr apps' own v3 APIs, which Nodearr calls, are not affected ([#43](https://github.com/Nodearr/Nodearr/issues/43)).
- What came from Sonarr and nothing in Nodearr reaches is gone: 1,000 files of pages and parts no route led to, with the UI built from what is left byte for byte the same; Sonarr's series metadata client, whose host `skyhook.nodearr.com` serves nothing; and the S1 and S2 spikes, which the acceptance scripts and the transport replaced. The inherited API endpoints that looked up or added a series now answer that Nodearr does not, instead of failing on a request that could not succeed. Help links the API gives for the Custom Script and Webhook connections and for custom formats lead to Sonarr's wiki instead of a page that did not exist ([#41](https://github.com/Nodearr/Nodearr/issues/41)). The backend half is [#43](https://github.com/Nodearr/Nodearr/issues/43).

## 0.2.7 (pre-release)

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

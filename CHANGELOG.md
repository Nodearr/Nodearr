# Changelog

What changed in each release, newest first. Each item links to the decision record that explains it, and the [V1 plan](docs/plans/v1-implementation-plan.md) has the whole story, slice by slice.

## Unreleased

### New

- **Whisparr v3 (Eros) and Whisparr v2.** Films are found by ID and scenes by Whisparr's own text search: studio, date, title and code. A v2 and a v3 find each other's scenes. Adult content is only ever found by Whisparr's searches, never by Radarr's ([ADR 0015](docs/adr/0015-the-indexer-id-matching-release-titles-and-candidate-ids.md), amendments).
- **Lidarr.** Artists, or single albums of them, are shared, whole albums are offered and compared by release group, and an album is moved as a folder of tracks that appears only once every track is checked. Lidarr finds albums by artist and album ([ADR 0020](docs/adr/0020-lidarr-albums-shared-and-moved-as-folders-of-tracks.md)).
- **Prowlarr.** Nodearr adds itself to Prowlarr as an indexer, to be searched there with your others, and switches off the copies Prowlarr would make in your apps ([ADR 0007](docs/adr/0007-prowlarr-as-downstream-indexer-consumer.md), amendments).
- **A Windows installer**, as Sonarr has: Nodearr in Program Files, started in the tray at sign-in, as a service, or not at all, with the firewall rules it needs.
- **A first run that sets itself up.** A welcome, sign-in set up first, and three steps on Home: connect your apps, name your node, connect with a friend. Home then becomes an overview.
- **System pages:** Tasks, Backup, Events and Log Files. A backup holds the node's key, so a restored node is the one its friends know ([ADR 0013](docs/adr/0013-node-identity-pairing-and-trust.md), amendments). Where backups go, how often and for how long are on Settings > General.
- **Send to a friend.** "I have that, let me send it to you": pick a title, or some seasons, episodes or albums of it, and a friend, or someone new, who is sent an invite made for it alone. It is shared with them on a list of their own, and their Home shows what arrived. **Get it** on that card adds it to their Sonarr, Radarr, Lidarr or Whisparr v3 with choices filled in the way most of their library is, monitors what was sent, and searches, so the app fetches it from the friend ([ADR 0021](docs/adr/0021-sending-a-title-to-a-friend.md)).
- **Posters.** Catalog > Peer Catalog shows each title's poster, and a grid view to browse a friend's catalog by title. The pictures come from the friend's own Arr apps and are kept once fetched; no image service is contacted ([ADR 0022](docs/adr/0022-posters-from-friends-arr-apps.md)).
- **Pairing attempts are limited.** A node that keeps failing is turned away for 15 minutes, and System > Status warns of it ([ADR 0013](docs/adr/0013-node-identity-pairing-and-trust.md), amendments).
- **A user guide** in `docs/user-guide/`, and `SECURITY.md`.

### Changed

- **A new friend is offered your whole library, except Hidden**, unless you untick that on the invite, or when you use theirs. It used to be nothing until you shared something. Friends you already have keep what they had ([ADR 0014](docs/adr/0014-sharing-rules-and-catalog-exchange.md), amendments).

- An **upgrade** is now what your Arr app's own quality profile would take, not any higher resolution or better source ([ADR 0014](docs/adr/0014-sharing-rules-and-catalog-exchange.md), amendments).
- Every package and the image carry **third-party notices** for everything they contain, and the .NET runtime's own licence and notices.
- The assembly copyright credits Sonarr again.
- The error reporting code inherited from Sonarr, which never ran, is removed from the web UI and the server, with the Sentry packages ([ADR 0018](docs/adr/0018-diagnostics-and-no-calls-home.md), amendments).
- The API description, `openapi.json`, is generated from the code, and covers Nodearr's own endpoints. It was Sonarr's.
- An album's tracks arrive named as Lidarr names its own, `Artist - Album - 01 - Title`, from the track titles a friend's Lidarr knows ([ADR 0020](docs/adr/0020-lidarr-albums-shared-and-moved-as-folders-of-tracks.md), amendments).

### Fixed

- The same bytes offered as two different episodes were shown to the Arr apps as one release, so a search for the other episode found nothing. A file offered by several friends is still one release.

- A node could end up with two Hidden lists, when two requests made one at the same moment. It now has one, and a node that had two keeps everything that was on either.
- Sonarr, Radarr and Prowlarr were always told to reach Nodearr at port 6633, whatever port it listened on.
- Several help texts gave the wrong page or the wrong meaning: Sync Categories, the library and data folder warnings, and the Sharing page.

## 0.1.0 (2026-10-15, pre-release)

The first release: everything V1 set out to do.

- **Sonarr and Radarr**: their libraries read, kept current by a webhook, and Nodearr registered in each as a pinned Newznab indexer and a SABnzbd download client ([ADR 0010](docs/adr/0010-arr-contract-newznab-indexer-and-sabnzbd-download-client.md)).
- **Friends**: one-time invites, trust, revoking and blocking, over iroh, with each node's key as its identity ([ADR 0013](docs/adr/0013-node-identity-pairing-and-trust.md)).
- **Sharing**: share lists by series, season, episode or movie, a Hidden list, and offers built for each friend, sent as changes after the first time ([ADR 0014](docs/adr/0014-sharing-rules-and-catalog-exchange.md)).
- **Searching**: by ID, scene numbering and air date, with release titles the Arr apps read correctly. The same file from several friends is one result ([ADR 0015](docs/adr/0015-the-indexer-id-matching-release-titles-and-candidate-ids.md)).
- **Transfers**: direct only, never through a relay, checked by BLAKE3 hash, resumed after a stop, and carried on from another friend with the same file ([ADR 0011](docs/adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md), [ADR 0016](docs/adr/0016-transfers-and-the-download-client.md)).
- **Diagnostics**: whether a friend is reachable directly, and warnings for a library Nodearr cannot read or a download folder an Arr app cannot see. No calls home ([ADR 0018](docs/adr/0018-diagnostics-and-no-calls-home.md)).
- **Settings > Node and Settings > General**, with environment variables that fix a setting in place ([ADR 0019](docs/adr/0019-node-settings-saved-in-the-ui-fixed-by-the-environment.md)).
- **Packages** for Linux, macOS and Windows, and an image for amd64 and arm64 ([ADR 0017](docs/adr/0017-one-docker-image-and-how-it-is-deployed.md), [releases](docs/guides/releases.md)).
- Run between two home networks with no port forwarded.

# Changelog

What changed in each release, newest first. Each item links to the decision record that explains it, and the [V1 plan](docs/plans/v1-implementation-plan.md) has the whole story, slice by slice.

## Unreleased

### New

- **Share everything, from the apps you choose.** Under each friend on Catalog > Sharing, a tick for each Arr app says which libraries "everything except Hidden" takes in, so you can share everything but Whisparr. Lists still share from any app, and Hidden still wins. **Share With New Friends**, on each app in Settings > Apps, says which apps a new friend gets: all but Whisparr unless you change it. Friends you have keep what they had ([ADR 0014](docs/adr/0014-sharing-rules-and-catalog-exchange.md), amendments).

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

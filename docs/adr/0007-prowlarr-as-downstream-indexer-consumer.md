# ADR 0007: Prowlarr as Downstream Indexer Consumer

## Status

Accepted.

## Context

[Prowlarr](https://prowlarr.com/) is an indexer manager in the Arr ecosystem. It can help users configure indexers once and share them with Arr applications.

Prowlarr does not own the user's media library, file paths, quality profiles, or completed-download import workflow. Treating it as a catalog source would put Nodearr on the wrong side of the architecture.

## Decision

Prowlarr is downstream of Nodearr.

Nodearr may expose a Torznab/Newznab-compatible indexer API that Prowlarr can consume. Prowlarr may then make Nodearr searchable by [Sonarr](https://sonarr.tv/), [Radarr](https://radarr.video/), and future supported Arr applications.

Nodearr must not import catalog data from Prowlarr and must not treat Prowlarr as a source of media library truth.

## Consequences

### Positive

- Nodearr fits existing indexer-manager workflows.
- Users who already centralize indexers in Prowlarr can add Nodearr there.
- Catalog source responsibility remains with Sonarr, Radarr, and future media-owning Arr apps.

### Negative / trade-offs

- Nodearr still needs direct configuration options for users who do not use Prowlarr.
- Prowlarr compatibility may expose edge cases in Torznab/Newznab responses.
- Search/grab testing must cover both direct Arr and Prowlarr-mediated flows.

## Alternatives considered

### Prowlarr as catalog source

Rejected. Prowlarr does not track the user's owned library in the way Nodearr needs.

### Require Prowlarr

Rejected. Nodearr should work when configured directly in supported Arr apps.

## Follow-up work

- ~~Test Nodearr indexer configuration through Prowlarr.~~ Done: the S1 spike, and the amendment below.
- ~~Document direct Arr setup and Prowlarr-mediated setup separately.~~ Done: the [user guide](../user-guide/connecting-apps.md#prowlarr).
- ~~Keep catalog import settings separate from indexer exposure settings.~~ Done: Prowlarr is added as an application that holds no library (amendment below).

## Amendments

- 2026-10-15: **Nodearr adds itself to Prowlarr.** Prowlarr is an application in **Settings > Apps**, as Sonarr and Radarr are, and Nodearr adds itself there as a Generic Newznab indexer called Nodearr, so it can be searched in Prowlarr with the user's other indexers. It reads nothing from Prowlarr: no library, no download folder check, and it does not count towards connecting Sonarr or Radarr on **Home**.
  - Prowlarr copies each of its indexers into every Sonarr and Radarr it manages. Such a copy is not pinned to Nodearr's download client (ADR 0010, finding 6), so a grab through it could go to a real [SABnzbd](https://sabnzbd.org/) and fail. Nodearr's indexer in Prowlarr therefore uses a sync profile of its own, also called Nodearr, with RSS, automatic search and interactive search off. The copies are made and are switched off. Sonarr and Radarr go on using the indexer Nodearr adds to them directly, pinned to its download client, which is why they still have to be added to Nodearr as well.
  - With **Full Sync**, Nodearr puts the indexer and the profile back as it set them, so a profile switched on by hand is switched off again. Deleting Prowlarr in Nodearr takes both out.
  - Run against Prowlarr 2.6.5 managing a Radarr 6.4.4: the indexer and the profile were added, the copy in Radarr arrived with all three switched off, a search in Prowlarr reached Nodearr, and deleting Prowlarr in Nodearr removed both.

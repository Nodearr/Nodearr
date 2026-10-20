# ADR 0002: Profiles, Matching, and Content Hashes

## Status

Accepted.

## Context

Nodearr discovers and transfers media candidates among trusted peers. A candidate must represent both a media item that an Arr app understands and an exact file that can be transferred and verified.

Hash-first identity is attractive in peer-to-peer systems, but it is the wrong top-level product model for Nodearr. Users and Arr apps ask for media items and upgrades according to local policy. They do not begin by asking for a content hash.

## Decision

Nodearr will separate media matching from file exactness.

Profiles decide desirability. Hashes decide exactness.

The matching stack is:

1. **Media match**: the same movie, episode, album, track, book, or other media item as defined by source Arr metadata.
2. **Profile match**: acceptable quality, language, custom format, release, or other local policy.
3. **Release candidate**: the specific peer-offered file that can satisfy the request.
4. **File identity**: the exact bytes represented by that candidate.
5. **Transfer verification**: proof that the received bytes match the advertised file identity.

Nodearr file identity should include:

- Whole-file content hash.
- File size.
- Hash algorithm/version.
- Verification state.

Chunk hashes and chunking strategy are future transfer-provider concerns for resumability and multi-source transfer.

Content hashes are not Nodearr's top-level media identity. They are used for verification, dedupe, exact-copy detection, resumability, and future multi-source transfer.

## Consequences

### Positive

- Nodearr aligns with how Arr apps and users reason about wanted media.
- Local profiles and quality policy remain meaningful.
- Transfers can still be verified independently.
- Future multi-source or resumable transfer can be added without making hashes the product identity.

### Negative / trade-offs

- Nodearr needs a clear media/profile matching layer before hash verification is enough.
- Different Arr apps expose different quality/profile metadata.
- A candidate can be media-correct but later fail byte verification.
- Hash calculation still adds I/O cost once verification support is added.

## Alternatives considered

### Content hash as top-level identity

Rejected. It is useful for exactness, but it does not answer whether a peer has an acceptable copy of the media item an Arr app wants.

### Filename/path identity

Rejected. Filenames and paths are fragile across peers and unsafe for verification.

### Metadata-only identity

Rejected. Multiple files can represent the same media item with different quality, edition, language, subtitles, or corruption state.

### Torrent info-hash as the only identity

Rejected for the core architecture. A torrent-like provider may exist later, but Nodearr's domain model must not depend on one transfer backend.

## Follow-up work

- ~~Define normalized media and profile match inputs from [Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/).~~ Done: the catalog (ADR 0006) and quality rankings read from each profile (ADR 0014, amendments).
- ~~Choose the first whole-file hash algorithm.~~ Done: [BLAKE3](https://github.com/BLAKE3-team/BLAKE3) (ADR 0012, ADR 0016).
- ~~Define file identity schema and migration/versioning.~~ Done: a file's hash kept with its size and modified time, worked out in the background (ADR 0016, amendments).
- ~~Define how release candidate GUIDs map to media matches and file identities.~~ Done: candidate IDs, ADR 0015.

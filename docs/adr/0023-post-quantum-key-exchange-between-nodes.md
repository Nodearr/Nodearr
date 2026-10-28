# ADR 0023: Post-Quantum Key Exchange Between Nodes

## Status

Accepted on 2026-10-28 ([#24](https://github.com/Nodearr/Nodearr/issues/24)). Checked on the wire between this release's sidecar and 0.6.0's the same day.

## Context

Everything between two nodes travels inside iroh's QUIC connection, encrypted with TLS 1.3 (ADR 0009, ADR 0011): pairing, catalogs, offers, sends and the files themselves. The keys for each connection come from a key exchange at its start.

Up to 0.6.0 the sidecar built iroh with its default `ring` crypto provider. With `ring`, rustls offers only X25519, P-256 and P-384. A large enough quantum computer breaks all three. Someone who records traffic between nodes today could decrypt it once they have one ("harvest now, decrypt later"): what friends share, what they send each other, and the files.

rustls's other provider, `aws-lc-rs`, also offers X25519MLKEM768: X25519 and ML-KEM-768 together, so the result is safe if either holds. It is the hybrid Chrome, Firefox and Cloudflare use, and TLS gives it the code point 0x11EC. iroh can be built with either provider.

## Decision

The sidecar builds iroh with `aws-lc-rs` and offers X25519MLKEM768 first.

- **iroh's default features, with `tls-aws-lc-rs` in place of `tls-ring`.** `tls-ring` must stay off: iroh's `Minimal` preset, which the sidecar uses, picks `ring` whenever that feature is on, even alongside `aws-lc-rs`.
- **rustls's `prefer-post-quantum`** puts the hybrid first. A client sends a hybrid key share and a plain X25519 one in the same ClientHello.
- **No change to what nodes agree on otherwise.** Two updated nodes agree on X25519MLKEM768. An updated node and an older one agree on X25519, with no extra round trip, so friends can update at different times. Nothing is negotiated in Nodearr itself: it is TLS's own group choice.
- **Node identity is unchanged.** Each node still proves its node ID with its Ed25519 key during the handshake (ADR 0013). A quantum computer breaks Ed25519 too, but that is impersonation while one exists, not decryption of what was recorded before. Post-quantum signatures are not yet part of mainstream TLS or of iroh.
- **The C toolchain.** AWS-LC is a C library, which `aws-lc-sys` builds with each target's own C compiler, without CMake. Only 32-bit Windows needs more: NASM, which the release workflow fetches pinned by SHA-256.

## Evidence

- A unit test, `key_exchange_offers_x25519mlkem768_first`, fails to build without the `aws-lc-rs` provider, and fails if X25519MLKEM768 stops being offered first or X25519 is dropped. `cargo tree -i ring --target all` finds no `ring` for any target.
- Release run [40698789263](https://github.com/Nodearr/Nodearr/actions/runs/40698789263) built the sidecar for all seven release targets (Linux musl x64, ARM64 and ARMv7, macOS x64 and ARM64, Windows x64 and x86) and all nine packages.
- **On the wire**, on 2026-10-28: three sidecars in [Docker](https://docs.docker.com/) on one network, with relays and port mapping off. Two were from that release run and one was 0.6.0's, from its package as checked against the release's `SHA256SUMS`. Each node was asked to connect to another, and the UDP between them captured. The handshake is in QUIC's Initial packets, whose keys come from the connection ID, so `tshark` reads it without any secret. Every connection was QUIC version 1 over a direct path, with one ClientHello:

  | Connection | ClientHello: groups (key shares) | ServerHello: group |
  | --- | --- | --- |
  | updated → updated | 0x11EC, X25519, P-256, P-384 (0x11EC, X25519) | **0x11EC, X25519MLKEM768** |
  | updated → 0.6.0 | 0x11EC, X25519, P-256, P-384 (0x11EC, X25519) | X25519 |
  | 0.6.0 → updated | X25519, P-256, P-384 (X25519) | X25519 |

- **The relay**, the same day: each sidecar was started with n0's relays and its TLS to them captured. The updated sidecar listed X25519MLKEM768 first in every ClientHello to `usw1-1` and `use1-1.relay.n0.iroh.link`. Every ServerHello chose X25519, including one from `usw1-1` that answered a ClientHello carrying a hybrid key share. A server that preferred the hybrid could have asked for it with a HelloRetryRequest; none did. 0.6.0's offered no hybrid. See the first consequence below.
- **An updated node and a 0.6.0 node pair and transfer.** `acceptance/Run-S7Transfers.ps1` passed all 33 checks with 0.6.0's sidecar on the receiving node, and again with it on the sending node: pairing, files imported byte for byte by real Sonarr and Radarr, resumes, limits, the upload line and ending trust.

## Consequences

### Positive

- What two updated nodes say to each other, and the files they move, stay secret from someone who records them now and has a quantum computer later.
- Nothing to set up and nothing to agree: two updated friends get it, and a friend who has not updated yet still connects as before.
- It covers what passes through a relay too. A relay forwards the two nodes' own QUIC packets, encrypted under their hybrid key, not its own.

### Negative / trade-offs

- **The relay hop itself is classical with n0's relays today.** They chose X25519 when offered the hybrid. That TLS protects only what the relay sees anyway: which node IDs connect, from which addresses (see [privacy and security](../user-guide/privacy-and-security.md)). A relay of your own built with `aws-lc-rs` would agree on the hybrid.
- A connection between an updated node and an older one is no better than before, until both update.
- The sidecar is larger: 16.1 to 17.0 MB on Windows x64, 17.5 to 19.6 MB on Linux musl x64.
- Releases take longer, and need a C toolchain everywhere and NASM for 32-bit Windows. The slowest sidecar job went from about 5 to about 6.5 minutes.
- AWS-LC is a large C library, maintained by AWS, where `ring` was mostly Rust.

## Follow-up work

- Post-quantum identity, when iroh supports post-quantum signatures: a node's key, and the proof of it in the handshake.

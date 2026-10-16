# ADR 0012: A Custom Transfer Protocol on Iroh Instead of iroh-blobs

## Status

Accepted on 2026-10-13, together with ADR 0011. The protocol was built into the sidecar in S7 (ADR 0016) and carries real imports between two nodes on one machine, and between two machines on different networks (ADR 0011, "Runs between two machines").

## Context

S2 had to choose how file bytes move once two endpoints are connected: adopt `iroh-blobs` and add authorization through its hooks, or define Nodearr's own protocol on a QUIC stream under its own ALPN.

The transfer has to satisfy rules that are specific to Nodearr:

- Authorize every request against peer trust and share lists, and re-check on each request (plan S7).
- Hold file data back unless the selected path is direct (ADR 0011).
- Serve files where they already are, in the Arr library, without importing them into another store.
- Verify with a whole-file BLAKE3 hash that is fixed when the transfer starts (plan decision 3).

## Decision

V1 uses a custom protocol on a plain Iroh bidirectional stream, under a Nodearr ALPN.

The spike's protocol is the starting point: the fetcher opens a stream and sends a request, the provider authorizes the remote endpoint ID and answers with a header carrying size and BLAKE3 hash, then sends the file in chunks that are each gated on the direct-path rule. The fetcher writes to a partial file, verifies the hash, renames the file into place and sends a one-byte verdict.

`iroh-blobs` is not used in V1. It stays a candidate for later, for resumable and multi-source transfer.

## Reasons

Findings from reading `iroh-blobs` 0.103.1 and from the spike:

| Need | Custom protocol | iroh-blobs 0.103.1 |
| --- | --- | --- |
| Maturity | Built on `iroh` 1.3.0, which is past 1.0 | Its own README says this version "is not yet considered production quality" and points to 0.35, which predates Iroh 1.0 |
| Authorization | Checked on accept with the remote endpoint ID. Proven in the spike. | Available through `ConnectMode::Intercept` and `RequestMode::Intercept` |
| Direct-path gating | A check before each chunk, with access to the connection's paths. Proven in the spike. | There is a per-chunk `ThrottleMode::Intercept` hook, but its event carries only a connection number, a request number and a size. It has no handle to the connection, so path checks would need a side table Nodearr maintains itself. |
| Files stay in the Arr library | Reads the file where it is | Needs its own store (a redb database plus hash outboards). Files can be referenced instead of copied, but every shared file must still be imported first. |
| Verification | Whole-file BLAKE3, as the plan decided | BLAKE3 with verified streaming, which is stronger: corruption is caught per chunk |
| Resume after restart | Has to be built (S7) | Built in, by range |
| Dependency size | About 620 lines in the spike | About 21,600 lines, plus redb and bao-tree |

The two things `iroh-blobs` does better, incremental verification and resume, are real. They do not outweigh a pre-production dependency that needs a second store and makes the direct-only rule harder to enforce.

The door stays open. The root hash `iroh-blobs` uses for a blob is the plain BLAKE3 hash of the file, the same value V1 stores. A later move to verified streaming would not change file identity or invalidate hashes already exchanged between peers.

## Consequences

### Positive

- Authorization, gating and failure reporting live in code Nodearr owns and can test.
- The only transport dependency is `iroh` itself, pinned.
- No second copy of media metadata to keep in sync with the Arr libraries.

### Negative / trade-offs

- Nodearr has to build resume itself. Without it, a transfer interrupted at 99% starts again.
- With one hash for the whole file, corruption is detected only at the end, after every byte has been received.
- A hand-written wire protocol needs versioning from the first release. The ALPN carries a version for that reason.

## Follow-up work

- ~~S7: define the real request (candidate ID and authorization context instead of the spike's single file), range requests for resume, and cancellation.~~ Done: ADR 0016.
- ~~S7: decide where the sidecar boundary sits (plan decision 4): the spike binary already has the provide and fetch halves that the .NET host would supervise.~~ Done: ADR 0013, "The sidecar boundary".
- Revisit `iroh-blobs` when it declares a production-quality release on Iroh 1.x.

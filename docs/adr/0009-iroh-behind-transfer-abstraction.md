# ADR 0009: Iroh as Leading Transport Direction Behind an Abstraction

## Status

Accepted for current design direction.

## Context

Nodearr needs endpoint identity, encrypted connections, peer discovery, NAT traversal, and direct peer-to-peer transfer support. Implementing those primitives from scratch would be risky and outside Nodearr's main product value.

[Iroh](https://www.iroh.computer/) is a strong candidate because it provides endpoint identity, encrypted QUIC connections, discovery, NAT traversal, direct connection support, and file/blob transfer primitives.

Iroh can also support relay-oriented architectures, but Nodearr's official policy rejects official media-byte relay.

## Decision

Iroh is the leading peer transport direction for real direct P2P discovery and transfer.

Nodearr will keep Iroh behind a Nodearr transfer abstraction. The domain layer should express operations such as:

- Connect to trusted peer.
- Request candidate.
- Authorize request.
- Transfer bytes.
- Verify result.
- Report failure reason.

The transfer provider decides how to use Iroh or another implementation to attempt direct connectivity.

Nodearr policy must be able to reject relayed media-byte transfer through official infrastructure even if a lower-level transport library has relay capabilities.

## Consequences

### Positive

- Nodearr can rely on a purpose-built networking stack instead of inventing NAT traversal.
- Transfer implementation can evolve without changing catalog or Arr compatibility models.
- Direct-only official relay policy remains enforceable above the transport layer.
- Early milestones can use a simpler local transport before Iroh is integrated.

### Negative / trade-offs

- Iroh integration details remain open implementation work.
- The abstraction must expose enough diagnostics to explain connection failures.
- Policy enforcement must be explicit so transport fallback does not violate Nodearr's relay model.

## Alternatives considered

### Build custom UDP hole punching and transfer protocol

Rejected for current direction. It is high-risk infrastructure work and not Nodearr's core differentiator.

### Expose Iroh concepts directly in the domain model

Rejected. Nodearr should not couple catalog, matching, or Arr compatibility to a specific transport library.

### Use BitTorrent or magnet links as the core transport model

Rejected as the core architecture. A torrent-like provider could be explored later, but it does not provide Nodearr's desired trusted-peer authorization and direct-only policy model by itself.

## Follow-up work

- ~~Prototype a minimal transfer provider abstraction.~~ Done: S4 and S7 (ADR 0013, ADR 0016).
- ~~Validate Iroh direct connection behavior for common NAT environments.~~ Done: the runs between machines in ADR 0011.
- ~~Define direct-only policy enforcement points.~~ Done: in the sidecar, above iroh (ADR 0011, ADR 0012).
- ~~Define how transfer progress, cancellation, and failure details flow back to Nodearr jobs.~~ Done: ADR 0016.


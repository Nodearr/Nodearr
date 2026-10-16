# ADR 0008: Peer Connectivity, Rendezvous, and No Official Relay

## Status

Accepted.

## Context

Nodearr should connect trusted peers with minimal configuration. Requiring VPN setup or manual port forwarding by default would make the product harder to adopt.

At the same time, official Nodearr infrastructure must not become a media-byte relay. Relaying large media files would create cost, abuse, moderation, privacy, and reliability problems.

## Decision

Nodearr may use rendezvous/discovery infrastructure to help trusted peers find each other and establish direct connections.

Rendezvous/discovery infrastructure may carry:

- Invite IDs.
- Public node identities.
- Temporary session metadata.
- Observed addresses.
- Transport hints.
- Connection candidates.
- Expiration data.

Official Nodearr infrastructure must not relay media bytes.

If direct peer connectivity cannot be established, the transfer fails clearly unless the user explicitly configured their own alternate network path or self-hosted relay behavior.

## Consequences

### Positive

- Nodearr can aim for a low-configuration default experience.
- Official infrastructure remains limited to small metadata payloads.
- The project avoids operating a high-cost media relay.
- Failure behavior is explicit and easier to reason about.

### Negative / trade-offs

- Some peer pairs will not be able to transfer without user-owned networking changes.
- Clear diagnostics are required so users understand connectivity failures.
- Future self-hosted relay support must be carefully separated from official infrastructure.

## Alternatives considered

### Require VPN

Rejected as the default expectation. Users may choose VPNs, but Nodearr should not require one by default.

### Require manual port forwarding

Rejected as the default expectation. It should remain an advanced user option, not a baseline setup requirement.

### Official hosted media-byte relay

Rejected. It conflicts with Nodearr's infrastructure and abuse-risk model.

## Follow-up work

- ~~Define rendezvous metadata format.~~ Done: iroh's endpoint addresses, and the invite code (ADR 0013).
- ~~Define direct-connectivity diagnostics.~~ Done: ADR 0018.
- ~~Define transfer failure reasons for no route, auth failure, policy failure, and verification failure.~~ Done: ADR 0016.
- ~~Decide how self-hosted relay or alternate network path options would be represented if added later.~~ Done: a relay of your own, or none, on Settings > Node (ADR 0019).


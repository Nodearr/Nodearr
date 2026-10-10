# ADR 0011: Direct-Only Media Over Iroh, With Relays Used for Coordination

## Status

Proposed. The S2 spike passed every scenario that can be run on one machine on 2026-10-10. The cross-NAT run between two machines on different networks is still outstanding, and this ADR should not be marked Accepted until it passes.

## Context

ADR 0008 forbids relaying media bytes through official infrastructure. ADR 0009 picks Iroh as the transport and requires that the no-relay rule be enforceable even though Iroh can relay. The V1 plan (decision 8) sets the default: use n0's public relays for coordination only, send media only over direct paths, and let users configure their own relay.

S2 had to compare two ways of getting there and show, with relayed bytes counted, that a peer pair with no direct route fails clearly without any media crossing a relay.

- **Strict:** relays disabled entirely.
- **Coordinated:** relays enabled, but every chunk of file data held back unless the connection's selected path is direct.

## Decision

Use the coordinated mode by default.

1. Endpoints run with relays enabled. The default relay set is n0's, and a user can substitute their own relay URL.
2. The relay may carry the QUIC handshake, the transfer request, the file header (size and hash) and Iroh's hole-punching coordination. None of that is media.
3. The sender checks the connection's selected path before every chunk of file data and sends only when that path is a direct IP path. If no direct path is selected within a timeout, it resets the stream and closes the connection with the typed failure `NoDirectRoute`.
4. If the selected path stops being direct in the middle of a transfer, the sender resets the stream at once and fails with `DirectPathLost`.
5. The receiver enforces the same rule independently. If file data arrives while its selected path is a relay, it stops the stream and fails the transfer.
6. Strict mode stays available as a setting for users who want no relay contact at all and have a reachable address, such as a LAN peer or a forwarded port.

## Evidence

`spikes/iroh-direct` is a standalone Rust binary on `iroh = "=1.3.0"`. `Run-IrohSpike.ps1` runs it as provider and fetcher against a self-hosted `iroh-relay` 1.3.0 in dev mode. Relayed bytes are measured two ways that do not depend on each other: Iroh's per-path counters on both endpoints, and the relay server's own `relayserver_bytes_sent_total` metric.

Both endpoints ran on one Windows machine. The file was 16,777,216 bytes. To make the connection start on the relay the way it does across NATs, the fetcher was given a ticket without the provider's IP addresses.

| Scenario | Outcome | File bytes sent | File bytes moved while a relay path was selected | Bytes the relay forwarded |
| --- | --- | --- | --- | --- |
| Strict: relays disabled, addresses in the ticket | Completed | 16,777,216 | 0 | 0 |
| Coordinated: starts on the relay, data waits for the direct path | Completed | 16,777,216 | 0 | 17,926 |
| No direct route: provider reachable only through the relay | `NoDirectRoute` after the 12 s timeout | 0 | 0 | 18,402 |
| Control: the same relay-only peers with the rule switched off | Completed | 16,777,216 | 16,777,216 | 17,391,743 |
| Coordinated, n0's public relays | Completed | 16,777,216 | 0 | not visible (19 to 20 KB by the endpoints' own counters) |
| No direct route, n0's public relays | `NoDirectRoute` | 0 | 0 | not visible (10 to 19 KB by the endpoints' own counters) |

What this shows:

- The relay carries about 18 KB per connection whether the file is transferred or not. That is the handshake and coordination traffic. It is the same in the transfer that completed and in the one that sent no file data at all.
- In the coordinated transfer the provider sent 11,841 QUIC stream frames on the direct path and none on the relay path. Against n0's relays it sent two on the relay path, consistent with the two writes that make up the roughly 100-byte file header, which the rule allows.
- The control proves the measurement would catch a leak. With the rule off, the relay forwarded 17.4 MB.
- Against n0's relays the provider reached the first chunk 8 ms before a direct path was selected. The rule held the chunk for those 8 ms.
- A fetcher that is not on the provider's allow list is refused when the connection is accepted, with `NotAuthorized` and no file data.

## Strict mode compared with coordinated mode

| | Strict (relays disabled) | Coordinated (relays for coordination) |
| --- | --- | --- |
| Peers on the same LAN, or one with a forwarded port | Works | Works |
| Peers behind different NATs | Cannot connect. Hole punching needs a third party to coordinate it, and without a relay an endpoint learns its public address only if the router answers a port-mapping request. | Works when the NATs allow hole punching |
| Contact with third-party servers | None | The relay sees both endpoint IDs, both public addresses, and connection timing |
| What the peers must exchange | Reachable IP addresses, which go stale | An endpoint ID and a relay URL |
| How the no-relay rule is enforced | By construction. There is no relay path. | By the application. Iroh keeps the relay path open as a backup for the life of the connection. |

Strict mode gives the stronger guarantee and cannot meet the goal of working without port forwarding. Coordinated mode meets the goal and makes the guarantee Nodearr's job.

## Risks and open questions

1. **Cross-NAT is unproven.** Every result here is from one machine. The plan's pass criteria include a cross-NAT transfer and a relay-only failure between the owner's PC and a friend's. `spikes/iroh-direct/README.md` has the commands.
2. **The rule is enforced above the transport.** Iroh 1.3.0 uses multipath QUIC and keeps the relay path as a backup. If the direct path dies while data is in flight, QUIC can retransmit already-written data over the relay before the application reacts. The spike resets the stream when the selected path changes, which discards queued data, but this was not exercised: it needs a firewall rule or a second network to break a direct path mid-transfer. S8's "block mid-transfer" test must measure it against a self-hosted relay. If the leak is not zero, the options to investigate are a smaller send window and Iroh's `path_selector` hook, which is gated behind the `unstable-custom-transports` feature and outside its semver guarantees.
3. **Iroh maps a port on the user's router by default.** With the default `portmapper` feature, the endpoints in the spike advertised a public address even against a localhost relay, which means it came from UPnP, NAT-PMP or PCP on the router. This helps direct connectivity and matches the "no manual port forwarding" goal, but it changes router state without asking. Owner decision: keep it on by default, or make it a setting.
4. **Windows only.** The spike ran on Windows 11. The target for V1 is Docker on Linux, where host networking and UDP behave differently (S8).
5. **n0's relays are a dependency Nodearr does not control.** Their availability and terms are outside the project. The self-hosted relay worked with no code changes, which keeps the user-configured relay option in the plan cheap.

## Consequences

### Positive

- The default experience needs no port forwarding and no VPN.
- The promise in ADR 0008 is checkable: the relay's traffic for a connection is a fixed handshake-sized amount, independent of file size.
- "No direct route" is a distinct, typed failure that reaches the Arr history (see ADR 0010).

### Negative / trade-offs

- A relay operator learns which endpoints talk to each other and when, though not what they transfer.
- The guarantee depends on Nodearr's own gating code on both sides, so that code needs tests of its own.
- Some peer pairs will never get a direct path and can never transfer.

## Follow-up work

- Run the cross-NAT scenarios on two machines and record the results here.
- S7: carry the gating rule and the typed failures into the real transfer provider.
- S8: measure relayed bytes when the direct path is blocked mid-transfer.
- Decide the port-mapping default (risk 3).

# ADR 0011: Direct-Only Media Over Iroh, With Relays Used for Coordination

## Status

Accepted on 2026-10-13.

The S2 spike passed every scenario that can be run on one machine on 2026-10-10. Runs between two machines on different networks followed on 2026-10-12 and 2026-10-13 (see "Runs between two machines" at the end). In the last of them a PC behind a home router and a cloud machine that allowed no UDP in connected directly and moved a file, with nothing opened on either side, and where only a relay was available no file data moved.

That initial acceptance used a cloud firewall, not a second home router. The later [two-home run](#2026-10-14-two-home-networks) covers that case too. A node in [Docker](https://docs.docker.com/) Desktop is a harder case than the same node run on the machine itself.

## Context

ADR 0008 forbids relaying media bytes through official infrastructure. ADR 0009 picks [Iroh](https://www.iroh.computer/) as the transport and requires that the no-relay rule be enforceable even though Iroh can relay. The V1 plan (decision 8) sets the default: use n0's public relays for coordination only, send media only over direct paths, and let users configure their own relay.

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

> The spike was removed once the transport sidecar had replaced it ([#41](https://github.com/Nodearr/Nodearr/issues/41)). It is kept at [bf4476a](https://github.com/Nodearr/Nodearr/tree/bf4476a/spikes/iroh-direct).

`spikes/iroh-direct` is a standalone [Rust](https://www.rust-lang.org/) binary on `iroh = "=1.3.0"`. `Run-IrohSpike.ps1` runs it as provider and fetcher against a self-hosted `iroh-relay` 1.3.0 in dev mode. Relayed bytes are measured two ways that do not depend on each other: Iroh's per-path counters on both endpoints, and the relay server's own `relayserver_bytes_sent_total` metric.

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

1. **Cross-NAT is unproven.** Every result here is from one machine. The plan's pass criteria include a cross-NAT transfer and a relay-only failure between two machines on different networks. `spikes/iroh-direct/README.md` has the commands. The owner plans a first run with a laptop on a 5G hotspot or a VPN. Both are strict NATs with no port mapping, the hardest case: a pass there is strong evidence, and a `NoDirectRoute` there says little about two home networks. With a VPN on a laptop that is still on the home Wi-Fi, check that the selected path is not a LAN address, or the run tested nothing.
2. **The rule is enforced above the transport.** Iroh 1.3.0 uses multipath QUIC and keeps the relay path as a backup. If the direct path dies while data is in flight, QUIC can retransmit already-written data over the relay before the application reacts. The spike resets the stream when the selected path changes, which discards queued data, but this was not exercised: it needs a firewall rule or a second network to break a direct path mid-transfer. S8's "block mid-transfer" test must measure it against a self-hosted relay. If the leak is not zero, the options to investigate are a smaller send window and Iroh's `path_selector` hook, which is gated behind the `unstable-custom-transports` feature and outside its semver guarantees.
3. **Iroh maps a port on the user's router by default.** With the default `portmapper` feature, the endpoints in the spike advertised a public address even against a localhost relay, which means it came from UPnP, NAT-PMP or PCP on the router. This helps direct connectivity and matches the "no manual port forwarding" goal, but it changes router state without asking. Decided by the owner on 2026-10-10: on by default, with a setting to turn it off (`Nodearr__Transport__DisablePortMapping` until there is a settings page). Nodearr has no relay fallback, so a peer pair without a direct path cannot transfer at all, and a port mapping is what rescues pairs where hole punching alone fails. It does nothing behind carrier-grade NAT, such as a mobile hotspot.
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

- ~~Run the cross-NAT scenarios on two machines and record the results here.~~ Done: see [Runs between two machines](#runs-between-two-machines) below.
- ~~S7: carry the gating rule and the typed failures into the real transfer provider.~~ Done: ADR 0016.
- Measure relayed bytes when the direct path is blocked mid-transfer. Still open: the sidecar resets the stream when the path stops being direct, and run 3 below shows a connection surviving a rule change, but nobody has counted relay bytes in that case.
- ~~S8: a settings page for the port-mapping switch (risk 3).~~ Done: **Settings > Node** (ADR 0019).

## Amendment, 2026-10-29: control connections stay open while nodes are talking

Decided in [#123](https://github.com/Nodearr/Nodearr/issues/123).

Until 0.7.0 every control message (a ping, a catalog request, a poster, a title sent to a friend) opened a new connection to the other node and closed it after one answer. Each one paid for a handshake and hole punching, about 18 KB through the relay by the measurements above. Opening a friend's library is a catalog request and one poster request per title, so it could put hundreds of KB through n0's relay before anything else happened, and the relay saw a new connection for each.

Now the sidecar keeps one control connection open to each node it is asked to reach, and sends each message on a new stream on it:

1. **Only connections this node opens are kept.** Incoming connections are served as before. The receiving side already took many messages on one connection, so the wire protocol did not change, and nodes on older versions work with this unchanged.
2. **A connection nobody has used for 3 minutes is closed.** A quiet friend has no open connection, so the relay does not see two nodes connected for hours for nothing. The next message opens a new one.
3. **Trust is still decided per message.** Every message goes to Nodearr, which looks up the other node's current state. Nothing is trusted because the connection was.
4. **Blocking a node closes everything open to it at once**, control and file connections, whichever side opened them. Before, a block was checked only when a connection was accepted, which a long-lived connection would have got round. Revoking or deleting a node closes the connection kept to it.
5. **A kept connection that has died is replaced.** One that has closed is replaced before the message is sent. One that turns out to be dead while the message is on it, because the other node restarted or went quiet, is replaced and the message is sent once more. A refusal (`Blocked`, `NotAuthorized`) is not tried again.
6. **The connection check opens a connection of its own**, so it sees the route as a new connection would, and a kept connection that still works cannot hide a change such as a new firewall rule.

Two nodes that open connections to each other at the same moment each keep their own, until each is idle.

## Amendment, 2026-10-29: what may cross a relay

Decided in [#122](https://github.com/Nodearr/Nodearr/issues/122), together with the amendment above.

Point 2 let the relay carry the handshake, the transfer request and the file header. In practice, until 0.7.0 the control channel (`nodearr/control/1`) sent on whatever path iroh had selected, so catalogs, catalog changes, posters and "sent to a friend" could cross a relay. The relay could not read them, but it saw who talked to whom, when, and how many bytes, and catalog sizes and how often they sync hint at the size of a library. The V1 plan (decision 8) said relays were for coordination only, and the design overview disagreed with it.

The relay now carries what is needed to find a path, and to say why there is none. What a library holds waits for a direct path, as file data does:

| May cross a relay | Waits for a direct path |
| --- | --- |
| Pair, Ping and Revoke, and their answers. Hole punching and the **Relay only** status need them, so a relay-only friend can still pair and shows as **Relay only**. | Catalog, CatalogChanged, Poster and Sent, and their answers. Without a direct path within the direct-route wait (**Settings > Node**, 20 s by default), the exchange fails as `NoDirectRoute` and nothing of it is sent. |

1. **The check is on whoever sends content, per message, for as long as it is being sent.** A catalog request goes from A to B, but the catalog comes back from B to A, so B waits for a direct path before it writes its answer, not only A before it asks. With connections kept open, a connection can be direct for hours and then fall back to the relay, so when it opened says nothing. Checking once before writing is not enough either: until the other node has acknowledged every byte, QUIC may still send or resend some of it on whatever path is selected by then. So the sender watches the path until then, and resets the stream as soon as it stops being direct, which stops QUIC sending or resending the rest, as point 4 does for file data. What QUIC put on the relay in the moment before the reset is not ruled out, as it is not for file data (risk 2), and the receiver refuses it.
2. **The receiver checks too**, as point 5 does for file data, by counting the QUIC stream frames that arrived other than on a direct path, as the S2 spike did. It uses the connection's totals, which include paths that have closed, so frames that came through a relay path that has since gone still count. A message is taken to have arrived direct only if nothing on its connection, over the connection's whole life, arrived any other way. Counting around each message is not enough: frames of streams not yet accepted may already be in when one is accepted, and a first version that counted that way took relayed messages for direct (found in review, and reproduced in a test with 32 messages sent at once through a relay). An answer is checked over the time since its stream was opened, which none of its frames can come before.
3. **A node of this version does not make the other node refuse its content.** A kept connection that has sent anything other than directly, such as a ping before hole punching found the direct path, is not used for a message that may not cross a relay: a new connection is opened for it once the direct path is there. Waiting first means no handshake goes through the relay while there is no direct path to use. If the other node still refuses the message as relayed, it is tried once more on a new connection. A connection replaced this way is left to close when idle, since other exchanges may still be on it, but revoking or deleting the node closes it along with the kept one (found in a second review, which saw it stay open after a revoke).
4. **The sidecar still does not know what a message means.** Nodearr says, per request (`direct` on `/v1/request`) and per answer (`direct` on the answer to `/v1/incoming`), whether it needs a direct path, and the sidecar tells Nodearr whether each incoming message arrived direct. The sidecar enforces it. A held-back message or answer resets only its own stream, with `NoDirectRoute`, and one refused for coming through a relay with code 6, as for file data, so the connection stays open for pings.
5. **Friends on 0.7.0 or earlier** do not wait before answering, so their catalog may arrive through a relay and be refused. The friend shows that as its own reason: "Needs a direct connection. This friend's node is on an older Nodearr, or reachable only through a relay". What was already in their catalog is kept. No protocol changed, so an older friend still pairs, pings and transfers files as before.
6. **The Relay only warning** on **System > Status** and in the troubleshooting guide says that a relay-only friend's library cannot be browsed either.

See [what crosses the relay with connections kept open](#2026-10-29-what-crosses-the-relay-with-connections-kept-open) for the run against a self-hosted relay.

## 2026-10-29: what crosses the relay with connections kept open

Measured for both amendments together, on the branch that carries #123 and #122, with the review fixes to the second amendment's points 1 to 3 in. `acceptance/Run-S8Docker.ps1` runs two Nodearr images in Docker on networks of their own, joined by a direct network that the script can take away and by a self-hosted `iroh-relay` 1.3.0 (`acceptance/relay.Dockerfile`). The direct-route wait was 5 seconds. All 22 of the suite's checks passed.

The figures are the relay server's `relayserver_bytes_recv_total` plus `relayserver_bytes_sent_total`. A byte the relay forwards is counted once as it comes in and once as it goes out, so each figure is about twice what it forwarded. The S2 spike above counted `relayserver_bytes_sent_total` only.

| Window | What the nodes did | Relay counters, in plus out | What else showed |
| --- | --- | --- | --- |
| Direct route present | Ten catalog syncs in a row, B asking A | 30,028 bytes | One `control connection opened` in B's log for all ten. Every sync succeeded. |
| Relay only, quiet | Nothing asked of either node for 20 s, after a connection check that went through the relay | 1,058 bytes | The connection check answered, as **Relay only**. |
| Relay only | One catalog sync, B asking A, 5.1 s | 266 bytes | B's sidecar logged that it held the message back. A's sidecar logged no answer held back and no message refused, one of which it would have logged had the request reached it. B kept A's 7 items, and A's catalog on B said `NoDirectRoute: Needs a direct connection`. |
| Relay only | A grab of a 22 MB episode, three attempts, with the connection checks they ask for | 226,530 bytes, for the whole relay-only phase | `NoDirectRoute`, 0 bytes of the file moved. |

What this shows:

- **Nothing of a catalog crossed the relay.** With only a relay between the nodes, the sync put less through it in 5 seconds than the relay carries anyway in 20. The request was never sent, so A had nothing to answer.
- **Pings still cross it**, so a relay-only friend still shows as **Relay only**, and pairing, which needs no catalog, is unaffected.
- **The relay's share of a connection is paid once, not once per message.** The ten syncs opened one connection between them. Its handshake and hole-punching coordination went through the relay before the direct path was found, about 15 KB forwarded, in line with S2's 18 KB per connection. The syncs themselves went on the direct path. Before #123 each of the ten would have opened its own connection; that was not run here, so ten times as much is an estimate, not a measurement.

Two earlier runs the same day, on the build before the review fixes, gave the same figures within a few hundred bytes. In the first of them one check failed because the script read the catalog's error from `/nodes/peers`, which does not carry it, not because of anything it measured.

## Runs between two machines

### 2026-10-12: a home network and a cloud machine

Made by the project's owner with `docker/two-machine-test.py`.

- **Sending side:** a Windows PC at home, behind an ordinary router, with Nodearr in Docker Desktop on a bridge network with published ports. No port was forwarded on the router at any point.
- **Receiving side:** an EC2 instance, Linux, with Nodearr on the host's network.
- **Coordination:** n0's public relays, the default.

| Run | The cloud machine's firewall | Connection check | Transfer |
| --- | --- | --- | --- |
| 1 | No UDP allowed in | Relay only | Failed as `NoDirectRoute` after three attempts, with no bytes of the file moved |
| 2 | UDP 6633 allowed in from the home address | Direct | Completed and imported by [Radarr](https://radarr.video/): 87 MB in 32 seconds, search and import included |

What this shows:

- **The rule holds across the real internet.** With only a relay between the nodes they paired, exchanged catalogs and answered a connection check, and no file data moved. The failure reached Radarr with its name and with what the connection check had found.
- **A home router needed no port opened** when the other side could be reached. The home side's own outgoing packets made the opening its replies came back through.
- **The outcome of run 1 was predicted, and the cause is Docker Desktop, not the router.** Nodearr run straight on that PC sees one stable public port. The same Nodearr in Docker Desktop is given a different public port for every destination. The cloud machine learned the port the relay saw, which was not the port the PC used towards the cloud machine, so neither side's packets were let in by the other.

What it does not show: two nodes behind home routers reaching each other with nothing forwarded. That needs both sides to keep one public port, which rules out Docker Desktop on either side for that run.

### 2026-10-13: the same two machines, with nothing opened on either side

The same PC and the same cloud machine. This time Nodearr ran straight on the PC and not in Docker Desktop (`two-machine-test.py send --native`), where it reports one stable public port.

| Run | The cloud machine's firewall | Connection check | Transfer |
| --- | --- | --- | --- |
| 3 | UDP 6633 allowed in when the nodes connected, and the rule removed part way through the transfer | Direct | Completed and imported |
| 4 | No UDP allowed in at all. A new receiving node, newly paired, on a UDP port that had never had a rule or carried traffic | Direct | Completed and imported by Radarr: 87 MB in 32 seconds, search and import included |

Run 3 shows only that a connection already made is not cut when the rule goes. A firewall of this kind goes on allowing a flow it has seen, so it says nothing about making one. It is listed so that nobody takes it for more.

Run 4 is the one that counts. Neither side accepted traffic it had not asked for: no port was forwarded on the home router, and the cloud machine's firewall let in nothing but replies. Each node learned the other's public address through the relay, both sent, and each firewall let the other's packets in as answers to its own. The file moved directly, and Radarr imported it.

What this shows, with the runs above it:

- **Files move directly between two networks with nothing opened on either**, when each side keeps one public port for its traffic. Most home routers do.
- **Where that fails, nothing of the file moves and the failure says so.** Run 1 is that case.
- **How Nodearr is run matters as much as the router.** The same PC, router and far side gave "Relay only" from Docker Desktop and "Direct" from Windows itself.

What it still does not show:

- **Two home routers.** The far side had a public address of its own, translated one to one, behind a firewall that tracks connections. A second home router adds its own translation of ports. That is expected to work for a router that keeps one public port, by the same mechanism, and has not been run.
- **Networks that give a different public port to every destination**: some mobile and carrier networks, and Docker Desktop. Two such sides cannot connect directly, and one such side needs the other to be reachable. Nodearr reports this before a transfer is tried.

### 2026-10-14: two home networks

The first run between two homes, each behind its own ordinary router. Both machines ran Windows, with an early build of Nodearr, from before the public 0.1.0 release, and Radarr 6.4.4 straight on Windows from their release packages (`tools/two-machine-test.ps1`, the owner sending and a friend receiving). Neither owner forwarded a port.

The addresses below are documentation examples replacing the addresses recorded in the run.

| | The owner's side | The friend's side |
| --- | --- | --- |
| Its network, as Nodearr reported it | UDP works and the public port stays the same | UDP works and the public port stays the same |
| The address the other side reached | 203.0.113.10:6633 | 203.0.113.20:6633 |
| Firewall | Allowed `nodearr-transport` | A rule blocking `nodearr-transport` (Windows' question was cancelled) |

The connection check said Direct. Radarr on the friend's side searched, grabbed and imported the test film: 89,579,739 bytes in 32 seconds, search and import included, 24 seconds of it the transfer. The SHA-256 was the same at both ends.

What it shows: two nodes behind two home routers found a direct route and moved a file.

On its own it did not show that nothing was opened. Nodearr asks each router for a port mapping (UPnP, NAT-PMP or PCP) by default, and does not report whether one was granted, so either router may have opened 6633 on Nodearr's request. The second run settles that.

**The second run, with nothing opened.** Both sides were taken down and started again with port mapping off (`-NoPortMapping`), on UDP ports neither had used: 6655 on the owner's side, 6656 on the friend's. The owner's sidecar was checked to be running with `--no-port-mapping`. The connection check said Direct, at 203.0.113.10:6655 one way and 203.0.113.20:6656 the other. Radarr imported the film, 89,558,499 bytes in 32 seconds, search and import included, and the SHA-256 was the same at both ends.

So two nodes behind two home routers connect directly and move a file with nothing forwarded and nothing mapped on either side. Each router kept one public port for its node's traffic, which is what made it work.

Still open:

- **Why the friend's blocking rule did not matter.** Most likely because the friend's side opened the connections, and Windows lets in replies to traffic a program has sent. That has not been checked. A connection the owner's side opens first may still be refused there.

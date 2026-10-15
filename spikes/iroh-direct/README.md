# S2 spike: direct-only transfer over Iroh

A standalone Rust binary on `iroh` 1.3.0 that sends one file between two endpoints and reports how many bytes each network path carried. It holds file data back until the connection's selected path is direct, so a relay only ever sees the handshake and coordination traffic. The decisions it supports are [ADR 0011](../../docs/adr/0011-direct-only-media-over-iroh-with-relay-for-coordination.md) and [ADR 0012](../../docs/adr/0012-custom-transfer-protocol-instead-of-iroh-blobs.md).

**Status:** every scenario that fits on one machine passes. The cross-NAT run on two machines has not been done yet. See "Two machines" below.

## Build

Needs Rust 1.91 or newer.

```sh
cargo build --release --manifest-path spikes/iroh-direct/Cargo.toml
```

The target folder grows to about 800 MB. Set `CARGO_TARGET_DIR` to put it on another drive.

## One machine, with a self-hosted relay

This needs the `iroh-relay` server, same version:

```sh
cargo install iroh-relay --version 1.3.0 --features server --locked
```

```sh
pwsh spikes/iroh-direct/Run-IrohSpike.ps1
```

The script starts `iroh-relay --dev` on localhost, runs five scenarios, and compares each endpoint's own relay-path byte count with the relay server's `relayserver_bytes_sent_total` metric. Add `-IncludeN0` to run two more scenarios against n0's public relays. Only handshake and coordination traffic reaches them, and the binary refuses to run its relay-everything control experiment against them.

Results go to `.run/results.json`, with each side's event log next to it.

While it runs, Iroh asks the local router for a temporary UDP port mapping (UPnP, NAT-PMP or PCP). That is Iroh's default behaviour and ADR 0011 lists it as an open decision.

## Two machines

This is the part of S2 that is still open: a transfer between two PCs on different home networks, and a relay-only failure between them.

Build the binary on both machines, or copy the release binary to a machine with the same OS and architecture. Both commands below use n0's public relays for coordination, which is the default.

On the machine that has the file:

```sh
nodearr-iroh-spike provide --file some-large-file.bin --key-file provider.key
```

It prints a `bound` line containing a `ticket`. Send the ticket to the other person. Then, on the other machine:

```sh
nodearr-iroh-spike fetch --ticket "PASTE_TICKET_HERE" --out received.bin
```

The last line each side prints is the result. For a pass:

- `outcome` is `Completed` on both sides.
- `media_bytes_moved_while_relay_selected` is `0` on both sides.
- `relay_paths.tx_bytes` is a few tens of kilobytes, not the size of the file.
- `direct_paths.tx_bytes` on the provider is a little more than the file size.

Then repeat with the provider unable to offer a direct path:

```sh
nodearr-iroh-spike provide --file some-large-file.bin --key-file provider.key --no-ip
```

The fetch should end with `outcome` `NoDirectRoute` after about 20 seconds, with `media_bytes` `0` on both sides.

If the first run ends in `NoDirectRoute` instead of `Completed`, the two NATs did not allow a direct path. That is a real result worth recording, with the router models, because it bounds how often Nodearr will work without port forwarding.

The ticket contains the provider's endpoint ID, its relay URL and its IP addresses, including the public one. Treat it like any other message that reveals a home IP address.

## Options

```text
--relay disabled|n0|custom    which relays to use (default n0)
--relay-url URL               the relay for --relay custom
--no-ip                       bind no IP sockets, so only relay paths exist
--media direct-only|allow-relay
                              allow-relay is a control experiment and is refused with --relay n0
--direct-timeout SECONDS      how long to wait for a direct path (default 20)
--key-file PATH               keep the endpoint's secret key, and so its ID, between runs
--allow ENDPOINT_ID           provider only: restrict who may fetch (repeatable)
--once                        provider only: exit after one connection
```

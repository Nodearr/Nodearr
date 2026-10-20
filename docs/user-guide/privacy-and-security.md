# Privacy and security

Nodearr separates trust between friends from access to its web UI and API. This page explains what information leaves your node and how to protect it.

## What a friend sees

A friend's catalog contains only files you share with them. Each offer includes metadata their Arr apps use to identify and evaluate the file:

- Identity: title, year, provider IDs, season and episode details, studio and release date, or artist, album and track count.
- Release information: quality, resolution, codecs, audio channels, languages, custom formats, release group, size and any saved release name.
- File information: import time, extension such as `.mkv`, and hash.

They do not see where any file is stored, what it is called on your disk, what you hide, what you share only with others, what else is in your library, or who your other friends are.

Friends also receive your node's network addresses, including its public IP address and local network addresses, and its relay address. Invite codes contain these details too. Share invites only with people you trust with that information.

## What a friend can do

- Request a file you currently share with them. Nodearr checks trust and sharing permissions on every request. An old catalog entry does not grant access after you stop sharing the file.
- Peer trust gives no access to your folders, web UI, API or Arr apps. Those have separate access controls. Nodes talk to each other's transport: pairing, checking in, exchanging catalogs and removal notices, sending a title's note or poster, revoking trust, and requesting a shared file.

Revoking or blocking a friend ends all of it at once, including anything being sent to them.

## What the relay sees

By default, Nodearr uses public relays operated by [n0](https://n0.computer), the makers of [iroh](https://www.iroh.computer/). A relay can see which node IDs communicate, their IP addresses and when they connect. It may carry encrypted control messages, catalogs, posters and transfer headers, but cannot read their contents. Downloaded media travels directly between the two nodes.

To use your own relay, follow [iroh's relay setup guide](https://docs.iroh.computer/add-a-relay). Then select **A relay of your own** under **Settings > Node** and enter its **Relay Address**. You can also select **No relay** if both nodes have addresses the other can reach directly. The direct-only rule for media applies with any relay.

## What anyone else sees

Nodearr sends no update checks, error reports, analytics, news requests or time-service requests to the Nodearr project. These inherited calls were removed ([ADR 0018](../adr/0018-diagnostics-and-no-calls-home.md)). This does not hide your network traffic: friends and relay operators can see the connection metadata described above.

Your router also receives port-mapping requests unless you turn off **Ask the Router to Open the Port** under **Settings > Node**.

## Encryption and checking

- Every connection between nodes is encrypted (QUIC, TLS 1.3), and each side proves it holds the key for its node ID during the handshake. A node cannot pretend to be another without that node's key.
- Every file is checked against the [BLAKE3](https://github.com/BLAKE3-team/BLAKE3) hash its sender gave before sending. A file that does not match is thrown away and never reaches your Arr app.

## Keeping your node safe

- **The node's key** is stored in `node.key` in the data folder. Backups include it if it exists when the backup is made. Anyone with the key can impersonate your node to its friends. Keep the data folder and backups private.
- **Invite codes** work once, for 24 hours unless you choose otherwise. Until one is used, whoever holds it can pair with you. Send it only to the person it is for.
- **Sign-in.** Nodearr asks for a password on the first start. Keep it on, and do not make the web UI reachable from the internet. If you need to reach it from outside, put it behind a reverse proxy with HTTPS and its own sign-in, as you would [Sonarr](https://sonarr.tv/).
- **The API key** lets whoever has it do anything the web UI can. Your Arr apps are given it. Reset it under **Settings > General** if it leaks.
- **Your Arr apps' API keys** are kept in Nodearr's database, as [Prowlarr](https://prowlarr.com/) keeps them. Anyone who can read the data folder can read them.
- **Strangers trying to pair** are turned away after five failed attempts in 15 minutes, and **System > Status** warns you. An invite's secret is 256 bits and cannot be guessed.

## What you share

Nodearr moves files only between people who chose to trust each other, and only what each chose to share. What you share, and whether you may, is up to you.

## Reporting a security problem

See [SECURITY.md](../../SECURITY.md).

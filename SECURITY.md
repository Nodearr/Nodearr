# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately with GitHub's [private vulnerability reporting](https://github.com/Nodearr/Nodearr/security/advisories/new) for this repository, and include what you found, how to reproduce it, and which version or commit it affects.

Once a fix is released, the advisory is published, with credit to you unless you would rather not.

## What counts

Anything that lets someone:

- reach a node's files, offers, web UI or API without being trusted or signed in;
- pass as another node, or get around revoking or blocking;
- make a node send a file it does not share with them, or that is not the one they asked for;
- make a node accept a file that does not match its hash;
- learn more about a node's library than its owner shares;
- run code on a node, or crash it remotely.

Problems in Sonarr, Radarr, Prowlarr, Whisparr, Lidarr or iroh belong with those projects. Problems in how Nodearr uses them belong here.

## Supported versions

Only the latest release gets fixes. Nodearr does not update itself, so a fix reaches a node when its owner installs the new version.

## How Nodearr is built to be safe

[Privacy and security](docs/user-guide/privacy-and-security.md) describes what each party can see and do, and [ADR 0013](docs/adr/0013-node-identity-pairing-and-trust.md) and [ADR 0014](docs/adr/0014-sharing-rules-and-catalog-exchange.md) how trust and sharing are enforced.

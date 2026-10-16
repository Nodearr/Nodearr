# Privacy and security

Who can see what, and what keeps a stranger out.

## What a friend sees

A friend is offered only what you share with them, and the offer says only what their Arr apps need to find and judge it:

- what each file is: its title, year, IDs (TVDB, TMDb, IMDb, MusicBrainz, or Whisparr's), season and episodes, episode titles and air dates, or studio and release date, or artist, album and track count;
- how good it is: quality, resolution, codecs, audio channels, languages, custom formats, release group, size, and the release name your Arr app kept, if any;
- when your Arr app imported it, its extension, such as `.mkv`, and its hash.

They do not see where any file is stored, what it is called on your disk, what you hide, what you share only with others, what else is in your library, or who your other friends are.

They also learn how to reach your node: its addresses, including its public IP address and its addresses on your home network, and which relay it uses. A direct connection needs them, so every invite code carries them too. Give invites only to people you would give your address to.

## What a friend can do

- Ask for a file you share with them, and nothing else. Every request is checked at that moment: whether they are still trusted, and whether the file is still shared with them. A name for a file they were given earlier earns nothing once you stop sharing it.
- Not browse your folders, reach your web UI or API, or reach your Arr apps. Nodes only ever talk to each other's transport, which answers a handful of messages: pairing, checking in, what do you share with me, my offer changed, trust is revoked, and send me this file.

Revoking or blocking a friend ends all of it at once, including anything being sent to them.

## What the relay sees

By default your node uses the public relays of [n0](https://n0.computer), who make iroh. A relay sees that two node IDs talk, from which IP addresses, and when, and passes on the encrypted messages sent through it before a direct path forms. It cannot read them, and never carries a file: files only ever go directly between the two machines.

To keep even that to yourselves, run your own relay (iroh's `iroh-relay`) and set it on Settings > Node, or use no relay at all if your nodes can reach each other's addresses directly.

## What anyone else sees

Nothing. Nodearr makes no calls home: no update checks, no error reports, no analytics, no news or time services. The code it was derived from did all of these, and they were taken out ([ADR 0018](../adr/0018-diagnostics-and-no-calls-home.md)).

Your router sees Nodearr ask it to open a port, unless that is turned off on Settings > Node.

## Encryption and checking

- Every connection between nodes is encrypted (QUIC, TLS 1.3), and each side proves it holds the key for its node ID during the handshake. A node cannot pretend to be another without that node's key.
- Every file is checked against the BLAKE3 hash its sender gave before sending. A file that does not match is thrown away and never reaches your Arr app.

## Keeping your node safe

- **The node's key** is in `node.key` in the data folder, and in every backup. Whoever has it can be your node to your friends. Keep the data folder and the backups private.
- **Invite codes** work once, for 24 hours unless you choose otherwise. Until one is used, whoever holds it can pair with you. Send it only to the person it is for.
- **Sign-in.** Nodearr asks for a password on the first start. Keep it on, and do not make the web UI reachable from the internet. If you need to reach it from outside, put it behind a reverse proxy with HTTPS and its own sign-in, as you would Sonarr.
- **The API key** lets whoever has it do anything the web UI can. Your Arr apps are given it. Reset it under Settings > General if it leaks.
- **Your Arr apps' API keys** are kept in Nodearr's database, as Prowlarr keeps them. Anyone who can read the data folder can read them.
- **Strangers trying to pair** are turned away after five failed attempts in 15 minutes, and System > Status warns you. An invite's secret is 256 bits and cannot be guessed.

## What you share

Nodearr moves files only between people who chose to trust each other, and only what each chose to share. What you share, and whether you may, is up to you.

## Reporting a security problem

See [SECURITY.md](../../SECURITY.md).

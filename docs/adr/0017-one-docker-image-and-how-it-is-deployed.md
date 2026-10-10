# ADR 0017: One Docker Image, and How It Is Deployed

## Status

Accepted. Built and checked in S8 on 2026-10-10, on one machine. The run between two machines on different networks is still to be done (see `docs/guides/two-machine-test.md`).

## Context

Until S8 Nodearr ran from a build folder, with the transport sidecar built separately and pointed to by an environment variable. That is fine for development and no use to anyone else. Most people who run Sonarr and Radarr run them in Docker, with a shared downloads folder. Nodearr has to fit into that without asking them to learn anything new.

## Decision

### One image

`Dockerfile` at the repository root builds one image in four stages:

1. The web UI, with Node and Yarn.
2. The transport sidecar, with Rust.
3. The application, with the .NET SDK.
4. What runs: the ASP.NET runtime image with the three results copied in, and nothing they were built with.

The sidecar sits next to the application, where Nodearr already looks for it, so nothing has to be configured for the two to find each other.

### Two volumes

- **`/config`** holds the database and the node's key. It is the node: lose it and the node has a new identity that nobody trusts. A new container on the same `/config` is the same node, with its trust and its unfinished transfers.
- **`/downloads`** is where transfers land, under `incomplete` and then `complete` (ADR 0016). It must be the same folder Sonarr and Radarr have at `/downloads`. With that, Nodearr reports `/downloads/complete/...`, the Arr app finds the file there, and no path mapping is needed on either side.

### Libraries are mounted read-only

Nodearr reads the libraries to send files to nodes its owner shares with. It never writes to them. They are mounted at the same paths Sonarr and Radarr use, with `:ro`. Because the paths match, the catalog's paths are readable as they are, again with no path mapping.

### It does not run as root

The container starts as root only long enough to make `/config` and `/downloads` belong to `PUID:PGID`, then runs Nodearr and the sidecar as that user. The default is 1000:1000, which is what the common Sonarr and Radarr images use. Files Nodearr writes to `/downloads` have to be movable by the Arr apps, so the three should run as the same user.

### Networking

The web UI and the API the Arr apps call are on TCP 6633. Other nodes connect over UDP, and that port is now fixed, at 6633 too, so that it can be published or forwarded. Outside Docker it is still picked freely unless set.

Two ways to run it, both in `docker/docker-compose.yml`:

- **The host's network** (Linux). The node sees the machine's real addresses and can ask the router for a port mapping. This gives the best chance of a direct connection, and it is the recommended way.
- **A bridge network with TCP and UDP 6633 published.** This is the only choice on Docker Desktop. The node cannot ask the router for a port mapping from inside a bridge, so forwarding UDP 6633 on the router is up to the owner.

A node's name, as other nodes see it, is set with `Nodearr__Transport__NodeName`. A container's own hostname is a random string.

## Evidence

`acceptance/Run-S8Docker.ps1` builds the image and runs two complete deployments from `acceptance/docker-compose.s8.yml`: each a Nodearr container with its own Sonarr 4.0.20 and Radarr 6.4.4, its own `/downloads` volume and its libraries mounted read-only. Nothing runs on the host. The two sides can reach each other only over one network that stands in for a direct route, and through a relay container that stands in for a public relay.

- Nodearr and the sidecar run as user 1000, the key is private to that user, `/downloads` is writable and the libraries are not.
- Nodearr reads the libraries with no path mappings, and Sonarr imports with none.
- A grab in Sonarr and one in Radarr are fetched from the other deployment and imported, byte for byte.
- The receiving container is replaced in the middle of a transfer. The new one is the same node, still trusted, and carries on.
- A file hidden on the sending node is refused by the sending node, even though the receiver still held a ticket for it and had not heard it was hidden.
- A file changed on the sender after its hash was fixed fails verification, is deleted, and never reaches Sonarr.
- With the direct network unplugged and only the relay left, the transfer fails as `NoDirectRoute` after three attempts with nothing of the file received, and the relay's own counters show only handshake traffic.
- The sender blocks the receiver in the middle of a transfer. It is cut, and the receiver is offered nothing more.

## Consequences

### Positive

- One `docker build`, one image, one container beside Sonarr and Radarr.
- No path mappings in the usual layout.
- Updating is replacing the container.

### Negative / trade-offs

- **The image is about 490 MB.** Most of that is the .NET runtime and the application.
- **Bridge networking is second best.** It needs a port forwarded by hand, and Docker Desktop offers nothing else.
- **No image is published.** It has to be built from the repository, which takes several minutes the first time.
- **Only linux/amd64 has been built.** The sidecar and the application should build for arm64, and that has not been tried.
- **Settings are environment variables.** Relay, port, port mapping, node name, download folder and limits have no page in the UI.
- **The inherited update check and error reporting are still in the code**, pointing at nodearr.com hosts that do not exist yet. They fail quietly. They should be removed or made real before anyone else runs this.

## Follow-up work

- The two-machine run.
- A published image, built for amd64 and arm64.
- A settings page for the transport options.

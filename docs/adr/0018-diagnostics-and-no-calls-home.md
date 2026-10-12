# ADR 0018: Telling the Owner What Is Wrong, and Calling Nobody

## Status

Accepted. Built and checked on 2026-10-11.

## Context

Three things stood between a working V1 and a useful test between two homes.

- **A failed transfer explained nothing.** `NoDirectRoute` says the nodes could not reach each other directly. It does not say whether that was known beforehand, or which side to look at. "Check connection" said only that the other node answered, and a node answers happily through a relay.
- **The likeliest mistakes were invisible to the person who made them.** A library not mounted into Nodearr shows up on the *other* person's side, as a refused transfer. A download folder Sonarr cannot see shows up as transfers that succeed and imports that never happen.
- **Inherited code called home.** The code Nodearr came from asked a service for updates, the time and notifications, reported errors to a Sentry server, and loaded a tracking script in the UI. All of it pointed at nodearr.com hosts that do not exist. Error reporting and tracking were also forced on in development builds, whatever the setting said.

## Decision

### A connection check says how the nodes are connected

"Check connection" now does two things. It asks the other node to answer, as before. Then it holds a connection open for up to ten seconds and records the path it settles on:

- **Direct.** The nodes reach each other directly. A file can move.
- **Relay only.** The nodes can talk, through a relay. A file transfer would fail, and the page says so.

The result, with the address at the far end, is kept and shown on Nodes > Trusted Nodes.

The same page shows what the transport has worked out about this node's own network: whether UDP gets out, whether the network gives a different public port to every destination (which usually defeats direct connections unless a port is forwarded), the address the outside sees, and the relay in use. Two of those also raise a notice on System > Status.

A direct connection to any trusted node overrides the rest. It is proof that the network lets one through. This matters because "UDP does not get out" is also what a relay with no address discovery looks like, and the first version of this check raised a false alarm on a deployment where transfers were working.

### Setup mistakes raise a warning on the side that made them

- **The library cannot be read.** After each catalog refresh Nodearr tries to read a sample of up to 100 files for each Arr app, spread through the catalog. If any cannot be read it says how many, and names one.
- **The Arr app cannot see the download folder.** Nodearr makes an empty marker folder in its `complete` folder and asks each Arr app, through the app's own file browser API, whether it can see it there. Where the app looks takes the app's own remote path mappings into account. This is the only way to know the two really share the folder, as opposed to each having a folder of the same name.
- **The download folder cannot be written.** Found while making the marker.

### The warnings are shown

System > Status had never listed health messages. The page was a placeholder, and a warning showed only as a number beside "System" in the sidebar. It now lists them, and each of Nodearr's own has a button to the page where it is fixed. The link to a wiki that does not exist was taken off.

### Nothing is contacted that the owner did not point Nodearr at

Removed, not switched off:

- The update check, and the health check that used it. Updating is replacing the image.
- The time check and the notifications check.
- Error reporting. No Sentry target is registered.
- Analytics. The service that decides whether it is on now always says no, the tracking script is gone from the UI, and so is the setting.

What Nodearr contacts now: the Arr apps it is given, the nodes its owner trusts, and the relay it is configured with (n0's public relays by default, for coordination only, per ADR 0011).

Two inherited paths remain that only run when someone uses the feature behind them: series lookups in the inherited TV pages, and the proxy test when a proxy is configured.

## Evidence

- Unit tests for what a connection check records, the three health checks, and where an Arr app is asked to look.
- `acceptance/Run-S8Docker.ps1` gained four checks, all on real containers:
  - A connection check between the two deployments says "direct". With the direct network unplugged it says "relay", before the transfer that then fails as `NoDirectRoute`.
  - A third Nodearr, pointed at a Sonarr whose library and downloads it does not have mounted, is warned about both. The warnings name the file and the folder. A deployment set up properly shows no warnings at all.
  - That third node runs with full logging for the whole run. Every outgoing request it logs is to the one Sonarr it was given.

## Consequences

### Positive

- A person about to try a transfer between two homes can find out first whether it can work, and from which side the obstacle comes.
- The two commonest setup mistakes are caught on the side that can fix them.
- Nodearr can be given to someone else without it reporting anything about them to anywhere.

### Negative / trade-offs

- **The network summary depends on the relay.** With relays off there is nothing to report, and with a relay that offers no address discovery the UDP result is unreliable until a direct connection proves otherwise.
- **The library check samples.** In a large library a handful of unreadable files can be missed. It is there to catch a missing mount, which it will.
- **The download folder check writes a folder**, briefly, each time it runs.
- **No crash reports.** Problems will be reported by people or not at all.
- **The net report is an unstable iroh API**, acceptable because iroh is pinned to one exact version.

## Follow-up work

- ~~Use the connection check automatically after pairing, so the answer is there without asking.~~ Done, see the amendments.
- ~~Show the same path information on a transfer that failed.~~ Done, see the amendments.

## Amendments

- 2026-10-11: **"Relay only" raises a warning, and the warning names the firewall.** A trusted node that the last connection check reached only through a relay is now a warning on System > Status, by name. It is raised whatever else is known about the network, and a direct connection to some other node does not hide it. It goes when a later check finds a direct route, or trust ends. The message, and the text beside "Relay only" on Nodes > Trusted Nodes, say to check each machine's firewall for incoming UDP before forwarding a port.

  The reason: Windows had been blocking incoming traffic to the transport on the development machine for most of a day, and nothing noticed. Both host acceptance suites passed in that time, because both of their nodes run on the one machine. From another machine that node would have been "Relay only", with a message that pointed only at the router. Nodearr cannot tell a blocked firewall from a router that needs a port forwarded. It can name both, the cheaper one first. The two-machine guide has a section on it.

- 2026-10-11: **connections are checked without anyone asking.** Until now a node's route was looked at when someone pressed "Check connection" and when the transport started, and at no other time. So the relay-only warning above could be out of date in either direction, and a node that had just paired showed nothing. The same check now also runs:

  - a few seconds after two nodes pair, on both sides. The wait is there because the other side finishes pairing a moment later, and asked sooner it would refuse to answer.
  - every 15 minutes, for every trusted node. It is a scheduled task like the others, listed on System > Tasks.
  - when a transfer attempt fails as `NoDirectRoute`, `DirectPathLost` or `PeerUnreachable`, at most once every 45 seconds for the same node.
  - when a file arrives from a node the last check did not call direct. A file only ever arrives over a direct route, so the check was wrong or old.

  The Trusted Nodes page is told when a check ends, and fetches the list again.

- 2026-10-11: **a transfer that found no direct route says what is known about the route.** After the name and the sentence that were there before, a `NoDirectRoute` or `DirectPathLost` failure now adds two more. The first is what the last connection check found, and how long before the failure: a direct route, only a relay, nothing at all, or that no check was made. Since a failed attempt causes a check, by the time a transfer gives up the check is a fresh one. The second is where this node's own network points: at this node's router when the network gives a different public port to every destination, at this machine's firewall when UDP was not seen getting out, and otherwise at both firewalls and then the other node's router. The whole text goes to the Arr app with the failure, so it is in Sonarr's and Radarr's history too.

  It can only speak for this side. Nothing here can see the other node's network, and a firewall that blocks incoming UDP on this machine does not show from this machine either. The wording says "as far as it can tell" for that reason.

- 2026-10-11: **links that led nowhere are gone.** The inherited pages linked to a home page, a wiki, forums, a chat server, a donation page and a source repository under names Nodearr has never had anything at. None of them answered. On every page the app leads to they have been taken out, and not pointed somewhere else, because there is nowhere public to point: the "More Info" block on System > Status, the donate button in the header, the wiki links in the file browser, the first-run sign-in question and the "add application" list. The API no longer hands out a wiki address with a health message or a provider. Inherited pages that no route leads to still contain theirs.

# Troubleshooting

Start at **System > Status**. Nodearr checks for most of what goes wrong and says what it found there, and on Home under what needs attention. The logs are under System > Log Files, and in the `logs` folder of the data folder.

## Relay only

**Nodes > Trusted Nodes** shows a friend's connection as **Relay only**, or a transfer fails with `NoDirectRoute` or keeps failing with `DirectPathLost`.

The two nodes can talk through the relay, but files never go that way, so nothing can be fetched until they reach each other directly. Most home networks manage this on their own. When they do not, the cause is nearly always one of these, in this order:

1. **A firewall on either machine is dropping incoming UDP.**
   - Windows: the installer allows `nodearr-transport.exe` in. From the zip, Windows asks the first time Nodearr runs; if that was answered no, or nobody saw it, allow `nodearr-transport.exe` for UDP in Windows Defender Firewall, Allow an app.
   - Linux: allow UDP to Nodearr's port in `ufw`, `firewalld` or whatever the machine uses. The port is on Settings > Node. It is chosen at random unless you set one there, so set one first.
2. **Docker Desktop, on Windows or macOS.** It gives the container a different public port for every destination, which defeats the way two nodes find each other. Forward the UDP port (6633 in the example compose file) on the router to that machine, or run Nodearr outside Docker. System > Status says so when it sees this.
3. **A router that changes the public port for every destination**, as some mobile networks, campus networks and carrier-grade NAT do. System > Status says so when it sees this. Forward the UDP port on the router, if you can. If both friends are behind such networks, one of them needs a forwarded port.
4. **Port mapping is off or not working.** Nodearr asks the router to open the port (UPnP, NAT-PMP or PCP) unless Settings > Node says not to. Many routers have it switched off. Forwarding the port by hand does the same.

Forwarding a UDP port on one side is enough. After any change, press **Check connection** for that friend on Nodes > Trusted Nodes.

## A friend cannot be reached

Pairing says "Unable to reach the node that created this invite", or a friend's status says they were last seen long ago.

- Both nodes have to be running and online. A friend's node that is off cannot be reached until it is back.
- If neither side has a relay (Settings > Node, Relay: none), the two nodes have to already know an address each other can reach. With a relay, they find each other.
- **System > Status says the peer transport is not running.** Nodearr could not start `nodearr-transport`. It has to be next to Nodearr, or wherever `Nodearr__Transport__BinaryPath` points. If it was saved with settings it cannot start with, such as a UDP port already in use, Settings > Node says so on saving; change the setting back.
- **System > Status says UDP does not get out of this network.** Something blocks outgoing UDP: a firewall, or a network that only lets the web through. Nodearr needs UDP out to reach friends at all.

## The Arr app does not import what arrived

System > Status says *"<app> cannot see the folder Nodearr puts finished transfers in"*, or Activity > Transfers shows Completed and the app never imports.

The app has to see Nodearr's download folder at the path it expects. Mount the same folder into both, at the same path, or add a remote path mapping in the app, from Nodearr's path to its own. See [the two folders that have to line up](connecting-apps.md#the-two-folders-that-have-to-line-up).

If System > Status says Nodearr *cannot write to its download folder*, the folder is missing, full, or the user Nodearr runs as cannot write there. In Docker, check `PUID` and `PGID` and the folder's owner.

## Friends are refused your files

System > Status says *"Nodearr cannot read … files it checked from <app>"*, or a friend's transfers from you fail with `NotFound`.

Nodearr reads files from the paths the Arr app reports. Mount the library into Nodearr at the same paths, read-only, or add a path mapping under Settings > Apps. Catalog > Local shows which files can be read. On Windows, a Nodearr service may run as an account that cannot read the library or a network share: see [Installing](installing.md#windows).

## A friend sees nothing of yours

A new friend is offered everything except Hidden, unless that was unticked on the invite. On Catalog > Sharing, either mark the friend as offered everything except Hidden, or put things on a list and give it to them. Check that what you expect is not on Hidden. A friend from before this was the default keeps what they had: nothing until it is shared.

What you newly share reaches them at their next catalog sync, every 15 minutes unless they changed it, or at once if they press **Sync Peer Catalogs**.

## An Arr app does not find what a friend has

Check, in order:

1. **Catalog > Peer Catalog** lists the file. If not, your friend has not shared it with you, or your node has not synced since: press Sync Peer Catalogs.
2. Its status is **Missing** or **Upgrade**. **Not in library** means none of your Arr apps has the series, movie or album: add it there first. **Have** means you already have one at least as good.
3. **The app searched.** Nodearr's indexer has RSS off, so the app only finds things when it searches: for something just added, or with Search All Missing, or by hand. See [how your Arr apps find it](finding-and-fetching.md#how-your-arr-apps-find-it).
4. **The app wants it.** An interactive search shows Nodearr's results with the reasons the app turned any down: wrong quality for the profile, a custom format it scores below zero, a blocklisted release.
5. **The right app.** Adult content is only found by Whisparr, and albums only by Lidarr.
6. **Through Prowlarr?** The copies Prowlarr makes of Nodearr's indexer are switched off on purpose. The app has to be added to Nodearr directly. See [Prowlarr](connecting-apps.md#prowlarr).

## Pairing keeps being turned away

"The other node has turned away too many invites from this node lately": your node tried five codes in 15 minutes that did not work. Wait 15 minutes, then try a fresh code once.

If System > Status on **your** node says nodes you do not know keep trying to pair: someone is trying invite codes. An invite cannot be guessed, and each node that keeps trying is turned away for 15 minutes. Check who you gave invites to, and whether the node needs to be reachable from where they are.

## Locked out of the web UI

If you have forgotten the password, stop Nodearr and turn sign-in off for a moment:

- in `config.xml` in the data folder, set `<AuthenticationMethod>External</AuthenticationMethod>`, or
- start it with the environment variable `Nodearr__Auth__Method=External`.

Start it, set a new username and password under Settings > General, then put the setting back. External means Nodearr asks for no sign-in at all, because something in front of it does, so do this only on a network you trust, and only for as long as it takes.

## Something else

- **System > Status** lists everything Nodearr has noticed.
- **System > Events** lists what happened, newest first.
- **System > Log Files** has the logs. Raise Log Level to Debug or Trace under Settings > General to see more, and put it back afterwards.
- **System > Tasks** shows when each scheduled task last ran, and runs one now.

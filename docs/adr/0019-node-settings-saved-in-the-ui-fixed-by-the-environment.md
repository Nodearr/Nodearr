# ADR 0019: Node Settings Are Saved in the UI and Fixed by the Environment

## Status

Accepted. Built and checked on 2026-10-11.

## Context

How a node reaches other nodes, what it is called and where it puts transfers were all environment variables. That suits a container and nobody else: there was no way to see what was in force, no check that a value could work, and changing one meant restarting Nodearr by hand.

A settings page has to live alongside those variables, not replace them. A container is built around some of them. The image mounts `/downloads` and publishes UDP 6633, and a page that let someone change either would break a deployment that was working.

## Decision

### The settings on Settings > Node

| Setting | Default | Takes effect |
| --- | --- | --- |
| Name | The machine's name | At once |
| Relay: public, your own, or none | Public | Transport restarts |
| Relay address | | Transport restarts |
| UDP port | 0, chosen by the system | Transport restarts |
| Ask the router to open the port | Yes | Transport restarts |
| Wait for a direct route | 20 seconds | Transport restarts |
| Download folder | `downloads` in the data folder | At once |
| Upload limit | None | Transport restarts |
| Download limit (since #48) | None | Transport restarts |

### The environment wins, and the page says so

A setting given as `Nodearr__Transport__...` is the one in force, whatever was saved. The page shows its value with a note that it is set in the environment, and offers nothing to change. A save that tries to change one leaves it alone.

Everything else is saved in the database, beside the rest of what makes the node itself.

A relay setting in the environment that cannot be read is treated as the default, public relays. A typing mistake must not switch relays off without anyone noticing.

### Nothing is saved that cannot work

Every problem is reported at once, each against its own field, and nothing is saved until there are none.

- **Name.** At most 64 characters, on one line.
- **Relay address.** Needed for a relay of your own, and must be an http or https address.
- **UDP port.** 0, or 1024 to 65535. A port other than the one in use is tried first: if something else on the machine has it, it is refused.
- **Wait for a direct route.** 5 to 120 seconds.
- **Upload limit.** 0, or at least 16 KB/s.
- **Download limit** (since #48). 0, or at least 16 KB/s.
- **Download folder.** A whole path, not the root of a disk. It is created and written to before it is accepted. It cannot be changed while transfers are under way, because they know where they are by it.

A setting the environment fixes is not checked. It is not the user's to get wrong.

### A change that needs it restarts the transport, and only the transport

The transport is given its settings when it starts. When one of them changes, Nodearr stops the sidecar and starts it again with the new ones. Nodearr itself keeps running.

- The node keeps its key, so it is the same node and every trust stands.
- It tells the nodes it trusts its new address, as it does on any start.
- Transfers under way stop and are picked up again from what had arrived (ADR 0016).

The save waits up to 20 seconds for the transport to come back, and says whether it did. If it did not, the settings are saved and the page says the transport would not start with them.

The name and the download folder are read when they are needed, so they need no restart.

## Evidence

- Unit tests for what wins, what is refused and when the transport restarts.
- `acceptance/Run-NodeSettings.ps1`, 10 of 10, on two real nodes with real sidecars:
  - Five bad values sent together are all refused, each named, and nothing changes.
  - A port another program holds is refused.
  - A setting fixed in the environment stays as it is.
  - A new name is seen by a trusted node on its next contact.
  - A new port, limit and wait restart the sidecar. Its command line shows all three. It is the same node, and the trusted node reaches it directly at the new port without pairing again.
  - A new download folder is created and is what the download client API then reports.
  - Everything survives a restart of Nodearr, and clearing a setting restores its default.
- `acceptance/Run-S8Docker.ps1` checks the container case: seven settings are fixed there, the upload limit is changed in the UI, the transport restarts, and the two deployments still trust and reach each other.
- The page was used by hand: a refused port shows its message under the field, and a saved name appears on the Nodes page.

## Consequences

### Positive

- The settings in force can be seen, changed and trusted to work.
- A container keeps the settings it depends on.
- Changing how a node connects no longer means restarting Nodearr or pairing again.

### Negative / trade-offs

- **Two places a setting can come from.** The page makes plain which is in force, but someone has to know to look at the environment to change a fixed one.
- **A restart interrupts transfers**, briefly. They resume.
- **A relay address is checked for its shape, not that a relay answers there.** A wrong one is found out when the transport starts, and reported then.
- **The download folder is not moved.** Changing it changes where new transfers go. Nothing is carried across, which is why it is refused while any are under way.
- **The path to the sidecar binary is still only an environment variable.** It is about how Nodearr is installed, not how a node behaves.

## Follow-up work

- ~~Check that a relay answers before saving its address.~~ Done in [#48](https://github.com/Nodearr/Nodearr/issues/48) (amendments).
- ~~A download limit, to go with the upload limit.~~ Done in [#48](https://github.com/Nodearr/Nodearr/issues/48) (amendments).

## Amendments

- 2026-10-24 (#55): **connection checks and route changes are recorded at Info.** A check records whether the peer was reached directly or by relay, the selected remote address and the control request round-trip time. The sidecar records path events and connection close reasons; when a selected path disappears, the transfer wait uses this setting's **Wait for a Direct Route** value before it ends a stream that has no selected route.

- 2026-10-11: **Settings > General is a real page, on the same rule.** The sidebar entry led to a placeholder, so after the first-run sign-in set-up there was nowhere to change the password, read or replace the API key, move the page to another port, or turn on debug logging. The page [Sonarr](https://sonarr.tv/) had for this was still in the code. It is wired in, cut down to what Nodearr has: where the page is served (address, port, URL base, HTTPS), signing in, the API key, certificate checking, and logging. The proxy, update and backup sections are left out. Updating was removed (ADR 0018), and backups have no page to list or restore them yet.

  - **A setting given in the environment is shown and cannot be changed**, as on **Settings > Node**. Sent anyway, it is ignored and not written to the file, where it would do nothing now and take effect by surprise if the environment ever changed.
  - **The Docker image gives the page's port and address** (`Nodearr__Server__Port=6633`, `Nodearr__Server__BindAddress=*`), as it already gave the UDP port. A port changed in the page and not in the container's published ports would leave the page unreachable, with no page to change it back from. With the host's network on Linux, another port is chosen in the compose file, where the rest of the container is set up.
  - **Resetting the API key gives the new one to Sonarr and Radarr.** They hold it for the indexer and the download client Nodearr added. Every application set to **Full Sync** is updated before the reset is reported as done. One that cannot be reached is updated at its next sync and shows as failing until then. The key is checked afresh on every request, so the old one stops working at once and no restart is needed. The page loads itself again, because it was holding the old key.
  - **A key given in the environment cannot be reset**, and the page offers no button for it.
  - **Port, address, URL base and HTTPS take effect after a restart**, which the page offers. A restart is a stop and a start inside the same process. In a container the container keeps running.

  Restarting from the page crashed Nodearr when this was first tried. The thread that waits on the transport sidecar let the cancellation that stops it escape, and an exception that leaves a thread takes the process with it. An ordinary shutdown had been ending the same way, unnoticed because the process was going anyway. It is caught now.

  Checked by `acceptance/Run-GeneralSettings.ps1`: refusals, a fixed setting, a log level used at once, a port applied by two restarts in a row with the same process, node and single sidecar, signing in by form with a changed password, and a reset key that Sonarr's own tests of the indexer and download client then pass with. `acceptance/Run-S8Docker.ps1` checks the fixed settings and the restart in a container.

- 2026-10-12: **there is a sign-in page.** Nodearr makes a new install set up signing in by form, and then sends the browser to `/login`. No such page had ever been built in this code base, so a fresh install answered its own first request with a 404. It was found on the first real deployment. Nothing before had loaded the page: every acceptance node is told in its environment that signing in is handled elsewhere, and the check added with the General page posted a username and password to `/login` without ever asking for the page.

  The page is one file, `frontend/src/login.html`, which the build already copied for any HTML it found. It stands alone, because the app's scripts are not handed out before signing in. It posts back to the address it was opened at, which carries the page to go to afterwards. The General settings acceptance now loads it, and the [Docker](https://docs.docker.com/) run checks the image carries it.

- 2026-10-19: **the later settings and backup pages.** Catalog Sync Interval is a ninth setting on **Settings > Node**: 15 minutes by default, 10 to 120, or zero to stop scheduled catalog syncs ([ADR 0014](0014-sharing-rules-and-catalog-exchange.md), amendments). **Settings > General** now includes backup folder, interval and retention. **System > Backup** lists, creates, downloads and restores backups, including `node.key` when it exists ([the backup guide](../user-guide/backups.md)). These supersede the earlier note that backups had no page.
- 2026-10-23: **a download limit, and a relay asked before it is saved** ([#48](https://github.com/Nodearr/Nodearr/issues/48)). The two follow-ups above.
  - **Download limit.** Beside the upload limit, and kept the same way: the most each download from another node may take, 0 or at least 16 KB/s, `Nodearr__Transport__DownloadLimitKbps` in the environment, and the transport restarts to take it. The sidecar reads more slowly, and QUIC's flow control holds the sender back to match. `acceptance/Run-S7Transfers.ps1` sets one a quarter of the sender's upload limit and measures the transfer held to it.
  - **A relay of your own is asked before it is saved.** Nodearr asks it what iroh's own nodes ask a relay to measure how far away it is, `GET /ping` at the root of its host, which a relay answers with 200. An address where nothing answers, or where something else answers, is refused with what happened, against the relay address field, and nothing is saved. It is asked only when the address is new or relays of one's own have just been chosen, so a relay that is down for a moment does not stop the rest of the page being saved. The trade-off above, that a relay address is checked only for its shape, no longer holds.
  - **The relay the environment fixes is the one judged.** A relay address was required, and is now asked, only when the relay in force is one's own. Before, the relay sent with the save was used even where the environment fixed another.
- 2026-10-27: **a total upload limit** ([#84](https://github.com/Nodearr/Nodearr/issues/84), the first part of [#75](https://github.com/Nodearr/Nodearr/issues/75)). The upload limit holds each upload on its own, so a node sending to five friends sends up to five times it. **Total Upload Limit** holds all uploads together: 0 or at least 16 KB/s, `Nodearr__Transport__TotalUploadLimitKbps` in the environment, and the transport restarts to take it. The sidecar keeps one booking clock for every upload. Each chunk an upload sends books the time it takes at the total rate, after what is already booked, and the upload waits until then or until the per-upload limit allows, whichever is later. Uploads running at once therefore take turns chunk by chunk and share the total evenly. Time no upload used is not saved up, so an idle node does not burst above the limit when uploads start. `acceptance/Run-S7Transfers.ps1` sets one at half the sender's upload limit and measures two transfers held to it together, each getting a fair part.
- 2026-10-27: **most downloads at once** ([#86](https://github.com/Nodearr/Nodearr/issues/86), part of [#75](https://github.com/Nodearr/Nodearr/issues/75)). What was a fixed three in ADR 0016 is **Most Downloads at Once**: 3 by default, 0 for no limit, at most 100, `Nodearr__Transport__MaxDownloads` in the environment. It is read on every pass of the transfer queue, so it applies at once with no restart. Lowering it lets what is running finish. `acceptance/Run-S7Transfers.ps1` sets it to 1 and has Sonarr grab two episodes: the second shows as queued in Nodearr and in Sonarr's queue, starts once the first is done, and both are imported.

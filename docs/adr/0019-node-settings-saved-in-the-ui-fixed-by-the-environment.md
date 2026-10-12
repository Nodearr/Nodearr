# ADR 0019: Node Settings Are Saved in the UI and Fixed by the Environment

## Status

Accepted. Built and checked on 2026-10-11.

## Context

How a node reaches other nodes, what it is called and where it puts transfers were all environment variables. That suits a container and nobody else: there was no way to see what was in force, no check that a value could work, and changing one meant restarting Nodearr by hand.

A settings page has to live alongside those variables, not replace them. A container is built around some of them. The image mounts `/downloads` and publishes UDP 6633, and a page that let someone change either would break a deployment that was working.

## Decision

### Eight settings, on Settings > Node

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

- Check that a relay answers before saving its address.
- A download limit, to go with the upload limit.

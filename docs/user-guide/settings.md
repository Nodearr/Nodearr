# Settings

Every setting Nodearr has, where it is, and the environment variable that can fix it in place.

## Settings > Node

How this node presents itself, reaches other nodes, and stores what it fetches ([ADR 0019](../adr/0019-node-settings-saved-in-the-ui-fixed-by-the-environment.md)).

| Setting | Default | What it does | Takes effect | Environment variable |
| --- | --- | --- | --- | --- |
| Name | The machine's name | What your friends see this node called. | At once | `Nodearr__Transport__NodeName` |
| Relay | Public relays | **Public relays**: n0's, which help two nodes find each other and never carry a file. **A relay of your own**: one you run, at Relay Address. **No relay**: nodes behind home routers then usually cannot find each other. Use it only when both have addresses the other can reach. | The transport restarts | `Nodearr__Transport__Relay`: `n0`, `custom` or `disabled` |
| Relay Address | | The address of your own relay. | The transport restarts | `Nodearr__Transport__RelayUrl` |
| UDP Port | 0 | The port friends' nodes connect to. 0 lets the system pick one each time. Set one when you need to forward or allow it. | The transport restarts | `Nodearr__Transport__Port` |
| Ask the Router to Open the Port | On | Asks the router to open the UDP port (UPnP, NAT-PMP or PCP). | The transport restarts | `Nodearr__Transport__DisablePortMapping=true` turns it off |
| Wait for a Direct Route | 20 seconds | How long a transfer waits for a direct connection before it fails with `NoDirectRoute`. | The transport restarts | `Nodearr__Transport__DirectTimeoutSeconds` |
| Download Folder | `downloads` in the data folder | Where transfers are written: `incomplete` while they arrive, `complete` once checked. Your Arr apps must see this same folder. It cannot be moved while transfers are under way. | At once | `Nodearr__Transport__DownloadFolder` |
| Upload Limit | None | The most each upload to a friend may send, in kilobytes per second. 0 is no limit. | The transport restarts | `Nodearr__Transport__UploadLimitKbps` |
| Catalog Sync Interval | 15 minutes | How often to ask each friend what changed, like Sonarr's RSS Sync Interval: 10 to 120, or 0 to ask only when told or asked. | At once | |

When the transport restarts, connections drop for a moment and transfers carry on once it is back. If the new settings stop it starting, the page says so; change them back.

The node's ID is shown on the same page. It is its public key, and it never changes.

## Settings > General

Inherited from Sonarr, and the same there.

### Host

| Setting | Default | Environment variable |
| --- | --- | --- |
| Bind Address | `*`, every address | `Nodearr__Server__BindAddress` |
| Port Number | 6633 | `Nodearr__Server__Port` |
| URL Base | | `Nodearr__Server__UrlBase` |
| Instance Name | Nodearr | `Nodearr__App__InstanceName` |
| Application URL | | |
| Enable SSL, SSL Port, SSL Cert Path, SSL Key Path, SSL Cert Password | Off, 9898 | `Nodearr__Server__EnableSsl`, `__SslPort`, `__SslCertPath`, `__SslKeyPath`, `__SslCertPassword` |
| Open browser on start | Yes | `Nodearr__App__LaunchBrowser` |

A new port, bind address or URL base takes effect after a restart. If you change the port or the URL base, change **Nodearr Server** for each app under Settings > Apps too, so each one is told where to reach Nodearr now.

### Security

| Setting | Default | Environment variable |
| --- | --- | --- |
| Authentication | Forms, set up on the first start | `Nodearr__Auth__Method`: `Forms`, `External` or `None` |
| Authentication Required | Enabled | `Nodearr__Auth__Required`: `Enabled`, `DisabledForLocalAddresses` or `DisabledForLocalhost` |
| Username, Password | | |
| API Key | Made on the first start | `Nodearr__Auth__ApiKey` |
| Certificate Validation | Enabled | |

- **Forms** is a sign-in page. **Basic** is the browser's own sign-in box; Nodearr turns it into Forms, as Sonarr does. **External** asks for nothing, because something in front of Nodearr (a reverse proxy with its own sign-in) does. **None** asks for nothing at all.
- **Disabled for local addresses** lets anyone on your home network in without signing in, and **Disabled for localhost** only someone on the same machine. Your friends' nodes never use the web UI or the API: they talk to the transport, which only trusts nodes you paired with.
- **The API key** is what your Arr apps and Prowlarr use to reach Nodearr. Resetting it gives the new one to every app set to Full Sync. Change it by hand in any other.

### Logging

| Setting | Default | Environment variable |
| --- | --- | --- |
| Log Level | Info | `Nodearr__Log__Level`: `Info`, `Debug` or `Trace` |
| Log Size Limit | 1 MB, at most 10 | `Nodearr__Log__SizeLimit` |

Also only from the environment: `Nodearr__Log__Rotate` (how many log files are kept, 50 by default), `__ConsoleLevel`, `__ConsoleFormat` (`Standard` or `Clef`), `__SyslogServer`, `__SyslogPort`, `__SyslogLevel`, `__DbEnabled` and `__Sql`.

### Backups

| Setting | Default |
| --- | --- |
| Folder | `Backups` in the data folder |
| Interval | 7 days |
| Retention | 28 days |

See [Backups](backups.md).

## Settings > Apps

Your Arr apps and Prowlarr, and the path mappings. See [Connecting your apps](connecting-apps.md).

## Set by the environment

Any setting above with an environment variable can be given that way instead. Each is `Nodearr__<Section>__<Name>`, as in the tables, and wins over what is saved: the page shows it as set by the environment and offers nothing to change. That is how a container is set up without the UI, and how the image fixes the settings its mounts and ports are built around:

| In the image | Value |
| --- | --- |
| `Nodearr__Server__Port` | 6633 |
| `Nodearr__Server__BindAddress` | `*` |
| `Nodearr__Transport__Port` | 6633 |
| `Nodearr__Transport__DownloadFolder` | `/downloads` |

`Nodearr__Transport__BinaryPath` says where `nodearr-transport` is, when it is not next to Nodearr.

In the image, `PUID` and `PGID` set the user Nodearr runs as, and `TZ` the time zone, as in the linuxserver.io images of the Arr apps.

### Postgres

Sonarr can keep its database in PostgreSQL, and the code is still there: `Nodearr__Postgres__Host`, `__Port`, `__User`, `__Password`, `__MainDb` and `__LogDb`. Nodearr has only ever been run on SQLite, its default. Use Postgres at your own risk.

## On the command line

| Option | What it does |
| --- | --- |
| `-data=<folder>` | Where `config.xml`, `nodearr.db`, `node.key`, the logs and the backups are kept. The default is `C:\ProgramData\Nodearr` on Windows and `~/.config/Nodearr` elsewhere. |
| `-nobrowser` | Does not open a browser on start. |

## In config.xml

`config.xml` in the data folder holds the settings on Settings > General, as Sonarr's does. Edit it only while Nodearr is stopped. The settings on Settings > Node and the rest are in the database.

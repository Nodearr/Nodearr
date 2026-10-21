# Installing

Nodearr runs directly on Windows, Linux and macOS, or in [Docker](https://docs.docker.com/). Choose a setup that lets Nodearr read your Arr libraries and lets the Arr apps read Nodearr's download folder. Running it alongside your existing Arr apps usually makes this easier.

New to Nodearr? [Getting started](https://nodearr.com/getting-started/) walks through the whole setup, from installing to your first search.

## Choose your environment

For a native installation, download the package for your machine from [Packages and downloads](https://nodearr.com/download/). Before running it, compare its SHA-256 hash with the matching entry in the release's `SHA256SUMS` file. Docker uses the published container image instead.

<!-- tabs:platform -->

### Windows

1. Run the Windows installer (`.exe`) you downloaded. It installs into Program Files.
2. Keep **When you sign in, in the tray** selected. Nodearr runs as you and can read whatever your account can.
3. Finish the installer.

The installer adds two Windows Firewall rules: one letting UDP in to `nodearr-transport.exe`, which is how friends' nodes reach yours, and one opening the web port, 6633, so Arr apps on other machines or in Docker can reach Nodearr. Uninstalling removes both.

<details>
<summary>Choose a service or manual startup instead</summary>

Select **When the computer starts, as a Windows service** in the installer to run Nodearr even when nobody is signed in. It runs as `LocalService`, which may not be able to read your library or a network share.

If **System > Status** reports unreadable files when Nodearr runs as a service, open Windows **Services**, find **Nodearr**, open **Properties** and select **Log On**. Choose an account with access to the library.

Select **Not automatically** if you prefer to start Nodearr from the Start menu yourself.

</details>

The data folder is `C:\ProgramData\Nodearr`, whichever way it starts. Installing over an older version, or uninstalling, leaves it alone. 32-bit builds (`win-x86`) are available for older machines.

<details>
<summary>Use a portable ZIP or install a service manually</summary>

Unpack `Nodearr.<version>.win-x64.zip` anywhere and run `Nodearr.exe` for the tray, or `Nodearr.Console.exe` in a terminal. `ServiceInstall.exe` and `ServiceUninstall.exe`, run as administrator, add and remove the service and the firewall rules, as the installer does. Windows may ask about the firewall the first time Nodearr runs from the ZIP. Allow it, or friends cannot connect.

</details>

### Linux

Choose the archive for your machine:

| Archive | For |
| --- | --- |
| `linux-x64` | x86-64 with glibc: Debian, Ubuntu, Fedora and most others |
| `linux-arm64` | 64-bit ARM with glibc, such as Raspberry Pi OS 64-bit |
| `linux-arm` | 32-bit ARM with glibc |
| `linux-musl-x64`, `linux-musl-arm64` | Alpine |

The archive includes [.NET](https://dotnet.microsoft.com/), but the system still needs its [native Linux dependencies](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual#dependencies), including ICU and OpenSSL. ICU is `libicu72` on Debian bookworm and `icu-libs` on Alpine; check the package name for your distribution and version.

1. Extract the archive you downloaded. Replace `NODEARR_ARCHIVE` with its complete filename:

   ```sh
   tar -xzf NODEARR_ARCHIVE
   ```

2. Start Nodearr:

   ```sh
   ./Nodearr/Nodearr -nobrowser
   ```

3. Open UDP for `nodearr-transport` in any firewall on the machine, such as `ufw`, or friends cannot connect directly.

The data folder is `~/.config/Nodearr` for the user running Nodearr. If `XDG_CONFIG_HOME` is set, the default is `$XDG_CONFIG_HOME/Nodearr`. Use `-data=<folder>` to choose another location.

<details>
<summary>Start Nodearr with systemd</summary>

To start it with the machine, run it as a service under the same user as your Arr apps. The following systemd unit is an untested example based on [Sonarr](https://sonarr.tv/)'s. Replace `media` with an existing user and group, put the application in `/opt/Nodearr`, and make `/var/lib/nodearr` writable by that user before using it:

```ini
[Unit]
Description=Nodearr
After=network-online.target

[Service]
User=media
Group=media
Type=simple
ExecStart=/opt/Nodearr/Nodearr -nobrowser -data=/var/lib/nodearr
TimeoutStopSec=20
KillMode=process
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

</details>

### macOS

> [!NOTE]
> The macOS packages are built by the release workflow but have not yet been run on a Mac.

1. Unpack `osx-arm64` (Apple silicon) or `osx-x64` (Intel).
2. macOS marks a downloaded archive as quarantined. Clear that mark before the first start, as with [Sonarr](https://sonarr.tv/):

   ```sh
   xattr -dr com.apple.quarantine Nodearr
   ```

3. Start Nodearr:

   ```sh
   ./Nodearr/Nodearr
   ```

The data folder is `~/Library/Application Support/Nodearr`, or wherever `-data=` points.

### Docker

The image is `ghcr.io/nodearr/nodearr`, for amd64 and arm64. This example adds Nodearr alongside your existing Arr apps.

1. Create a `docker-compose.yml` file, or add the `nodearr` service to your existing Compose file:

   ```yaml
   services:
     nodearr:
       image: ghcr.io/nodearr/nodearr:latest
       restart: unless-stopped
       network_mode: host
       environment:
         PUID: "1000"
         PGID: "1000"
         TZ: Etc/UTC
       volumes:
         - ./config/nodearr:/config
         - /path/to/shared/downloads:/downloads
         - /path/to/tv:/tv:ro
         - /path/to/movies:/movies:ro
   ```

2. Replace the example paths with your folders. `/downloads` must refer to the same host folder in Nodearr and every Arr app. For libraries, match the paths the Arr apps already use: if Sonarr sees its library at `/data/tv`, use `/path/to/tv:/data/tv:ro` in Nodearr. Keep only the library mounts you need, and add mounts for music or other libraries in the same way.
3. Set `PUID` and `PGID` to a user and group with permission to read the libraries and write to the shared download folder. Match your Arr apps' user and group where possible. Set `TZ` to your time zone.
4. Choose the networking for your host. On Linux, keep `network_mode: host`; it gives the best chance of a direct connection and lets Nodearr ask your router to open its UDP port. On Docker Desktop (Windows or macOS), or for bridge networking, remove that line and add this under the `nodearr` service instead:

   ```yaml
   ports:
     - "6633:6633/tcp"
     - "6633:6633/udp"
   ```

   With bridge networking, forward UDP 6633 on your router to the machine running Nodearr. Docker Desktop gives a container a different public port for every destination; without the forward, friends often cannot connect directly.

5. From the folder containing your edited Compose file, start Nodearr:

   ```sh
   docker compose up -d nodearr
   ```

> [!IMPORTANT]
> Keep `/config`, and back it up. It holds the database, settings and node key. A node that loses its key is a new node that none of its friends trust.

The image sets the web port to 6633, the bind address to all interfaces, the UDP port to 6633 and the download folder to `/downloads`. These settings appear as controlled by the environment in the UI.

<details>
<summary>Change the image defaults or start a new Arr stack</summary>

Override the corresponding environment variables in your Compose file if needed. For example, use `Nodearr__Server__Port` to change the web port with host networking. See [environment overrides](settings.md#set-by-the-environment).

The [complete example compose file](../../docker/docker-compose.yml) starts Nodearr, [Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/) together. Use it if you are creating a new stack; keep your existing app containers when adding Nodearr to a stack you already run.

</details>

<!-- /tabs -->

## The first start

<!-- setup:sign-in -->

1. Open `http://localhost:6633` on the machine running Nodearr. From another device, use that machine's address and port 6633.
2. Choose a username and password when prompted, then select **Save**. Nodearr stores your identity key and Arr API keys, so keep sign-in enabled. You can change authentication settings later under **Settings > General**.

<details>
<summary>See the first sign-in screen</summary>

![A new node asking for a username and password before anything else](images/first-run.png)

</details>

<!-- /setup:sign-in -->

On **Home**, connect your Arr apps, choose your name and connect with a friend. The [Getting started guide](https://nodearr.com/getting-started/) includes all of those steps and a first transfer on one page.

![Home's three steps on a new node: connect your Arr apps, choose your name, connect with a friend](images/first-steps.png)

### Choose your name

<!-- setup:name -->

On **Home**, under **Choose your name**, enter a name your friends will recognize and select **Use this name**. You can change it later under **Settings > Node**, in **Name**.

<!-- /setup:name -->

Once setup is complete, **Home** shows warnings, titles sent by friends, files you are missing, your library and your connections.

## Updating

Nodearr does not update itself. [Create a backup](backups.md#when-they-are-made) before updating. Install the new version over the old one, or pull the new Docker image and recreate the container using the same `/config` volume. Keep the existing data folder to preserve settings, friends and the identity key.

# Updating

Nodearr checks for a new version every 6 hours, as [Sonarr](https://sonarr.tv/) and [Radarr](https://radarr.video/) do. It asks `updates.nodearr.com`, which names the newest release and is signed by the Nodearr maintainer. What happens next depends on how you installed Nodearr:

| How you installed Nodearr | What happens |
| --- | --- |
| The Windows installer, in the tray or as a service | Nodearr can install the update itself. |
| A zip or archive on Windows, Linux or macOS | Nodearr can install the update itself, if it can change the folder it runs from. If it can't, it tells you and you update by hand. |
| [Docker](https://docs.docker.com/) | A container never updates itself. You pull the new image. |
| A package manager | The package manager updates Nodearr. |

Friends on older versions keep working with yours, but they don't get what's new. Keeping up helps them too.

## See what's available

Go to **System > Updates**. It shows the version you're running, when Nodearr last checked, and a newer version if there is one, with what changed in it. Select **Check Now** to check again.

**System > Status** shows a notice once an update has been available for 14 days.

## Install an update now

This works for the Windows installer and for a zip or archive Nodearr can change.

1. Go to **System > Updates**.
2. Under the new version, select **Install Now**.

Nodearr backs up its database, downloads the package for your platform and checks it against the size and SHA-256 that the signed update information gives. It then restarts as the new version. The page reconnects when Nodearr is back.

If the new version doesn't start and answer within 3 minutes, Nodearr puts the old version back, with its database and `config.xml` as they were, and starts it again. Why is in the updater's log: on **System > Updates**, select **Updater Log Files**. The backup made before the update is also under **System > Backup**.

## Let Nodearr install updates by itself

When **Install Updates Automatically** is on, under **Settings > General**, Nodearr installs a new version by itself at a quiet moment:

- It waits until no transfer is queued or running, nothing is being sent to a friend, and no catalog is being refreshed or exchanged. It looks again every 5 minutes.
- After 24 hours of waiting, it installs anyway. A transfer cut off by the restart continues from what had already arrived.
- A new major version, such as 1.0 after 0.x, is never installed by itself. Install it from **System > Updates**.

**Install Updates Automatically** is on by default on Windows and off elsewhere, as in Sonarr. A container never updates itself, whatever the setting says.

## Update the Docker container

**System > Updates** shows the `docker pull` command for the new version. Before you update, [create a backup](backups.md#when-they-are-made).

<!-- tabs:docker-update -->

### Docker Compose

1. In the folder with your `compose.yaml`, pull the new image:

   ```sh
   docker compose pull nodearr
   ```

2. Recreate the container with it:

   ```sh
   docker compose up -d nodearr
   ```

### docker run

1. Pull the new image, using the command **System > Updates** shows, such as:

   ```sh
   docker pull ghcr.io/nodearr/nodearr:0.10.0
   ```

2. Stop and remove the old container, then start a new one with the same `docker run` command you used before, with the new tag and the same `/config` volume.

<!-- /tabs -->

Keep the same `/config` volume: it holds your settings, your friends and your node's key.

## Update by hand

If Nodearr can't install the update itself, **System > Updates** says why and links to the release page.

1. [Create a backup](backups.md#when-they-are-made).
2. Download the package for your platform from the release page or [Packages and downloads](https://nodearr.com/download/), and compare its SHA-256 with the release's `SHA256SUMS`.
3. Install it over the old version: run the Windows installer, or stop Nodearr and unpack the archive over the old folder.
4. Start Nodearr.

Keep the existing data folder to keep your settings, friends and node key.

> [!NOTE]
> Earlier versions of the Windows installer put Nodearr in Program Files. The installer now puts it in `C:\ProgramData\Nodearr\bin`, next to its data, as Sonarr and Radarr install theirs. Installing over an earlier version moves it there and updates the firewall rule. Your data in `C:\ProgramData\Nodearr` stays where it is.

## Turn update checks off

Under **Settings > General**, in **Updates**:

- **Check for Updates** off: Nodearr no longer asks `updates.nodearr.com`.
- **Contact nodearr.com** off: Nodearr contacts nothing on nodearr.com, whatever the other settings say.

Either can be set in the environment instead: `Nodearr__Update__Check=false` or `Nodearr__Network__ContactNodearr=false`. See [Settings](settings.md#updates).

With checks off, you hear about new versions only from the [changelog](https://nodearr.com/changelog/) or the GitHub releases page.

## What the check sends

The check is two plain requests for files, with no identifier, no query string and no cookies. [Privacy and security](privacy-and-security.md#what-anyone-else-sees) lists exactly what they carry.

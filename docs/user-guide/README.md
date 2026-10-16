# Nodearr user guide

For anyone running Nodearr beside their Arr apps. Each page is one task.

1. [Installing](installing.md): Docker, Windows, Linux or macOS, and the first start.
2. [Connecting your apps](connecting-apps.md): Sonarr, Radarr, Whisparr, Lidarr and Prowlarr, and the two folders that have to line up.
3. [Friends and sharing](friends-and-sharing.md): invites, trust, and choosing what each friend sees.
4. [Finding and fetching](finding-and-fetching.md): what friends have, how your apps find it, and what happens to a grab.
5. [Troubleshooting](troubleshooting.md): what each warning means, and what to do when a transfer fails.
6. [Settings](settings.md): every setting, and the environment variables that can fix one in place.
7. [Backups](backups.md): what a backup holds, and moving a node to a new machine.
8. [Privacy and security](privacy-and-security.md): what friends, the relay and anyone else can see.

New to it? [What Nodearr is](../../README.md), then [how it works](../overview-end-to-end-design.md), then start at 1.

## Words this guide uses

- **Node:** one running Nodearr. Yours, or a friend's.
- **Friend:** a node yours trusts, because one of you used the other's invite. The UI calls them trusted nodes.
- **Arr app:** Sonarr, Radarr, Whisparr or Lidarr: an app that holds a library. Prowlarr is added the same way but holds none.
- **Offer:** what a friend shares with you. Each friend's is built for you alone.
- **Transfer:** a file being fetched from a friend after one of your Arr apps grabbed it.
- **Relay:** a server that helps two nodes find each other. It never carries a file.

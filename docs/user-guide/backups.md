# Backups

## What a backup holds

A backup ZIP contains the node's settings (`config.xml`), SQLite database (`nodearr.db`) and, if it exists, identity key (`node.key`). The database holds your app connections, friends, sharing choices and transfer records. The key identifies your node to its friends.

Restoring a backup that includes `node.key` preserves that identity, so friends do not need to pair again. Older backups, or backups made before the transport first starts, may lack the key. Check the ZIP before using it to move a node. Without the original key, you must pair with each friend again.

> [!CAUTION]
> **Keep backups private.** Anyone who has one can pretend to be your node to your friends.

A backup excludes your media library and download folder. Back up those files separately if you need them.

## When they are made

![System > Backup, with Backup Now and Restore Backup, and the warning to keep backups private](images/backup.png)

- Scheduled backups run every 7 days and are kept for 28 days by default. Change **Folder**, **Interval** and **Retention** in the **Backups** section under **Settings > General**. Choose a folder on another disk if possible.
- To create a manual backup, go to **System > Backup** and select **Backup Now**. Do this before moving your node or making changes you may need to undo. Manual backups remain until you delete them; the retention setting applies only to scheduled backups.

**System > Backup** lists both kinds of backup and lets you download or delete them.

## Restoring

Restore only backups you made or obtained from someone you trust. Restored settings control how Nodearr runs.

1. Go to **System > Backup**.
2. Select **Restore** beside a listed backup. To upload a backup from elsewhere, select **Restore Backup** and choose its `.zip` file. You can also restore a `.db` database file alone, but that does not restore `config.xml` or `node.key`.
3. After Nodearr restarts, sign in with the credentials saved in the restored database.

The restored key takes effect when the transport restarts. Nodearr does not replace the identity of a running transport ([ADR 0013](../adr/0013-node-identity-pairing-and-trust.md)).

## Moving a node to a new machine

1. On the old machine, go to **System > Backup**, select **Backup Now** and download the backup. Check that the ZIP contains `node.key`.
2. **Stop the old node, and do not start it again.** Two nodes with the same key confuse every friend: each would reach whichever answered first.
3. Install Nodearr on the new machine ([Installing](installing.md)), start it, and sign in.
4. On the new node, go to **System > Backup**, select **Restore Backup** and upload the ZIP. After the restart, use the sign-in credentials from the old node.
5. Go to **Settings > Apps**. Check **Application Server** and **Nodearr Server** for each connection; either address may have changed. Save any changes to update Nodearr's entries in the Arr app. Also check **Path Mappings** and **Download Folder** under **Settings > Node**.
6. Go to **Nodes > Trusted Nodes** and select **Check connection** for each friend. **Direct** means file transfers can resume. Automatic connection checks run every 15 minutes; if a connection stays **Relay only**, see [Troubleshooting](troubleshooting.md#relay-only).

Your friends need to do nothing.

## Docker

The container's data folder is `/config`. Keep a copy of `/config/Backups` elsewhere, or stop the container and copy all of `/config`.

To move to another machine, stop the old container first. Either [restore a backup](#moving-a-node-to-a-new-machine) into a new container or move the entire `/config` volume. Check the app addresses, path mappings and download mount before resuming transfers. Keep the old container stopped so only one node uses the identity key.

## PostgreSQL

The database in these backups is SQLite, Nodearr's default. If you use the inherited [PostgreSQL](https://www.postgresql.org/) option, back up and restore that database separately: Nodearr's zip does not include it. See [Settings](settings.md#postgres).

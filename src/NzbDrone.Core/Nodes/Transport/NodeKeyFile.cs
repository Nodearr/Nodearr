using System;
using System.IO;
using NLog;

namespace NzbDrone.Core.Nodes.Transport
{
    // The node's secret key is the node's identity (ADR 0013). The sidecar makes it on first start
    // and reads it at every start, from the data folder.
    public static class NodeKeyFile
    {
        public const string FileName = "node.key";

        // A key from a restored backup waits here until the transport next starts, so that it never
        // changes under a running transport.
        public const string RestoreFileName = "node.key.restore";

        public static string GetPath(string appDataFolder)
        {
            return Path.Combine(appDataFolder, FileName);
        }

        public static string GetRestorePath(string appDataFolder)
        {
            return Path.Combine(appDataFolder, RestoreFileName);
        }

        // Called before the transport starts. Returns whether a restored key took the place of the old one.
        public static bool ApplyPendingRestore(string appDataFolder, Logger logger)
        {
            var restore = GetRestorePath(appDataFolder);

            if (!File.Exists(restore))
            {
                return false;
            }

            var path = GetPath(appDataFolder);
            File.Move(restore, path, true);

            // As the sidecar makes it: readable by its owner only.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            logger.Info("This node's key was restored from a backup");

            return true;
        }
    }
}

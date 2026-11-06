using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NzbDrone.Common.EnvironmentInfo
{
    public static class BuildInfo
    {
        static BuildInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();

            Version = assembly.GetName().Version;

            var attributes = assembly.GetCustomAttributes(true);

            Branch = "unknown";

            var config = attributes.OfType<AssemblyConfigurationAttribute>().FirstOrDefault();
            if (config != null)
            {
                Branch = config.Configuration;
            }

            Release = $"{Version}-{Branch}";

            // A development build has no release version, and is known by its first three numbers.
            var nodearrVersion = attributes.OfType<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "NodearrVersion")?.Value;
            NodearrVersion = string.IsNullOrWhiteSpace(nodearrVersion) ? Version.ToString(3) : nodearrVersion.Trim();
        }

        public static string AppName { get; } = "Nodearr";

        public static Version Version { get; }

        // The release version as it was tagged, with any pre-release tag: 0.10.0 or 0.10.0-beta.1.
        public static string NodearrVersion { get; }
        public static string Branch { get; }
        public static string Release { get; }

        public static DateTime BuildDateTime
        {
            get
            {
                var fileLocation = Assembly.GetCallingAssembly().Location;
                return new FileInfo(fileLocation).LastWriteTimeUtc;
            }
        }

        public static bool IsDebug
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }
    }
}

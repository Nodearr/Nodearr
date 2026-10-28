using NLog;

namespace NzbDrone.Core.Nodes.Transport
{
    public static class SidecarLogLine
    {
        public static (LogLevel Level, string Message) Parse(string line)
        {
            var content = line.TrimStart();
            var separator = content.IndexOf(' ');

            if (separator > 0)
            {
                var level = content.Substring(0, separator) switch
                {
                    "ERROR" => LogLevel.Error,
                    "WARN" => LogLevel.Warn,
                    "INFO" => LogLevel.Info,
                    "DEBUG" => LogLevel.Debug,
                    "TRACE" => LogLevel.Trace,
                    _ => null
                };

                var message = content.Substring(separator + 1).TrimStart();

                if (level != null && message.Length > 0)
                {
                    return (level, message);
                }
            }

            // Startup failures and output from an older sidecar must remain visible.
            return (LogLevel.Info, line);
        }
    }
}

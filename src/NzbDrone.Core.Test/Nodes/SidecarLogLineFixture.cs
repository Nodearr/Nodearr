using NLog;
using NUnit.Framework;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Test.Nodes
{
    [TestFixture]
    public class SidecarLogLineFixture
    {
        [TestCase("ERROR", "Error")]
        [TestCase(" WARN", "Warn")]
        [TestCase(" INFO", "Info")]
        [TestCase("DEBUG", "Debug")]
        [TestCase("TRACE", "Trace")]
        public void should_keep_the_target_message_and_fields_at_the_sidecar_level(string prefix, string expectedLevel)
        {
            const string message = "netwatch::interfaces::windows: failed to retrieve default route error=NoRoute {\\n    meta: Meta,\\n}";

            var entry = SidecarLogLine.Parse(prefix + " " + message);

            Assert.That(entry.Level, Is.EqualTo(LogLevel.FromString(expectedLevel)));
            Assert.That(entry.Message, Is.EqualTo(message));
            Assert.That(entry.Message, Does.Not.Contain("\n"));
        }

        [TestCase("Error: failed to bind the endpoint")]
        [TestCase("2026-10-28T05:04:19Z WARN netwatch: old sidecar")]
        [TestCase("\u001b[33m WARN\u001b[0m old sidecar", TestName = "should_preserve_old_ansi_output_at_info")]
        [TestCase("WARNING netwatch: unknown level")]
        [TestCase("WARN")]
        [TestCase("WARN   ")]
        [TestCase("  some unrecognized output")]
        [TestCase("")]
        public void should_relay_unrecognized_lines_unchanged_at_info(string line)
        {
            var entry = SidecarLogLine.Parse(line);

            Assert.That(entry.Level, Is.EqualTo(LogLevel.Info));
            Assert.That(entry.Message, Is.EqualTo(line));
        }
    }
}

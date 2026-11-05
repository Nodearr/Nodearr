using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NzbDrone.Core.Metrics
{
    // Writes metrics in Prometheus's text format, 0.0.4, or in OpenMetrics 1.0 for a client that
    // asks for it. Small enough not to need a library.
    // https://prometheus.io/docs/instrumenting/exposition_formats/
    // https://github.com/prometheus/OpenMetrics/blob/main/specification/OpenMetrics.md
    public static class PrometheusText
    {
        public const string TextContentType = "text/plain; version=0.0.4; charset=utf-8";
        public const string OpenMetricsContentType = "application/openmetrics-text; version=1.0.0; charset=utf-8";

        // Whether a scrape's Accept header asks for OpenMetrics.
        public static bool WantsOpenMetrics(IEnumerable<string> accept)
        {
            return (accept ?? Enumerable.Empty<string>())
                .SelectMany(a => (a ?? string.Empty).Split(','))
                .Any(a => a.Trim().StartsWith("application/openmetrics-text", System.StringComparison.OrdinalIgnoreCase));
        }

        public static string Write(IEnumerable<MetricFamily> families, bool openMetrics)
        {
            var text = new StringBuilder();

            foreach (var family in families)
            {
                var sampleName = family.Type switch
                {
                    MetricType.Counter => family.Name + "_total",
                    MetricType.Info => family.Name + "_info",
                    _ => family.Name
                };

                // OpenMetrics names the family; the text format names the sample, and has no info.
                var typeName = openMetrics ? family.Name : sampleName;
                var type = family.Type switch
                {
                    MetricType.Counter => "counter",
                    MetricType.Info => openMetrics ? "info" : "gauge",
                    _ => "gauge"
                };

                text.Append("# HELP ").Append(typeName).Append(' ').Append(EscapeHelp(family.Help, openMetrics)).Append('\n');
                text.Append("# TYPE ").Append(typeName).Append(' ').Append(type).Append('\n');

                foreach (var sample in family.Samples)
                {
                    text.Append(sampleName);

                    if (sample.Labels.Count > 0)
                    {
                        text.Append('{');
                        text.Append(string.Join(",", sample.Labels.Select(l => $"{l.Key}=\"{EscapeLabel(l.Value)}\"")));
                        text.Append('}');
                    }

                    text.Append(' ').Append(Number(sample.Value)).Append('\n');
                }
            }

            if (openMetrics)
            {
                text.Append("# EOF\n");
            }

            return text.ToString();
        }

        private static string Number(double value)
        {
            if (double.IsPositiveInfinity(value))
            {
                return "+Inf";
            }

            if (double.IsNegativeInfinity(value))
            {
                return "-Inf";
            }

            if (double.IsNaN(value))
            {
                return "NaN";
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string EscapeLabel(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        // The text format escapes only backslashes and line breaks in help; OpenMetrics quotes too.
        private static string EscapeHelp(string help, bool openMetrics)
        {
            var escaped = (help ?? string.Empty).Replace("\\", "\\\\").Replace("\n", "\\n");

            return openMetrics ? escaped.Replace("\"", "\\\"") : escaped;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Test.Metrics
{
    // A strict reader of what /metrics writes, by the rules of Prometheus's text format 0.0.4 and
    // of OpenMetrics 1.0, so a test fails on anything Prometheus would refuse to scrape.
    public static class ExpositionParser
    {
        private static readonly Regex MetricName = new(@"^[a-zA-Z_:][a-zA-Z0-9_:]*$");
        private static readonly Regex LabelName = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

        public class Sample
        {
            public string Name { get; set; }
            public Dictionary<string, string> Labels { get; set; }
            public double Value { get; set; }
        }

        public class Result
        {
            // By the name on the TYPE line.
            public Dictionary<string, string> Types { get; } = new();
            public List<Sample> Samples { get; } = new();

            public IEnumerable<Sample> Named(string name) => Samples.Where(s => s.Name == name);

            public double Value(string name, params (string Name, string Value)[] labels)
            {
                return Named(name).Single(s => s.Labels.Count == labels.Length && labels.All(l => s.Labels.TryGetValue(l.Name, out var v) && v == l.Value)).Value;
            }
        }

        public static Result Parse(string text, bool openMetrics)
        {
            var result = new Result();
            var helped = new HashSet<string>();
            var series = new HashSet<string>();
            string family = null;
            string familyType = null;

            if (!text.EndsWith("\n", StringComparison.Ordinal))
            {
                throw new FormatException("The last line has no line break.");
            }

            var lines = text.Substring(0, text.Length - 1).Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (openMetrics && line == "# EOF")
                {
                    if (i != lines.Length - 1)
                    {
                        throw new FormatException("# EOF is not the last line.");
                    }

                    return result;
                }

                if (line.Length == 0)
                {
                    if (openMetrics)
                    {
                        throw new FormatException("OpenMetrics allows no empty lines.");
                    }

                    continue;
                }

                if (line.StartsWith("# HELP ", StringComparison.Ordinal) || line.StartsWith("# TYPE ", StringComparison.Ordinal))
                {
                    var parts = line.Substring(7).Split(' ', 2);
                    var name = parts[0];

                    if (!MetricName.IsMatch(name))
                    {
                        throw new FormatException($"Not a metric name: {name}");
                    }

                    if (line[2] == 'H')
                    {
                        if (!helped.Add(name))
                        {
                            throw new FormatException($"Two HELP lines for {name}.");
                        }

                        Unescape(parts.Length > 1 ? parts[1] : string.Empty, openMetrics);
                    }
                    else
                    {
                        var type = parts.Length > 1 ? parts[1] : string.Empty;
                        var allowed = openMetrics
                            ? new[] { "counter", "gauge", "info", "stateset", "histogram", "gaugehistogram", "summary", "unknown" }
                            : new[] { "counter", "gauge", "histogram", "summary", "untyped" };

                        if (!allowed.Contains(type))
                        {
                            throw new FormatException($"Not a type: {type}");
                        }

                        if (result.Types.ContainsKey(name))
                        {
                            throw new FormatException($"Two TYPE lines for {name}.");
                        }

                        if (result.Samples.Any(s => Belongs(s.Name, name, type, openMetrics)))
                        {
                            throw new FormatException($"TYPE for {name} comes after its samples.");
                        }

                        result.Types[name] = type;
                        family = name;
                        familyType = type;
                    }

                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    if (openMetrics)
                    {
                        throw new FormatException($"Not an OpenMetrics line: {line}");
                    }

                    continue;
                }

                var sample = ParseSample(line, openMetrics);

                if (family == null || !Belongs(sample.Name, family, familyType, openMetrics))
                {
                    throw new FormatException($"{sample.Name} is not a sample of {family ?? "any TYPE"}.");
                }

                var key = sample.Name + "{" + string.Join(",", sample.Labels.OrderBy(l => l.Key, StringComparer.Ordinal).Select(l => $"{l.Key}={l.Value}")) + "}";

                if (!series.Add(key))
                {
                    throw new FormatException($"Two samples for {key}.");
                }

                if (familyType == "counter" && sample.Value < 0)
                {
                    throw new FormatException($"{key} is a counter below zero.");
                }

                result.Samples.Add(sample);
            }

            if (openMetrics)
            {
                throw new FormatException("No # EOF.");
            }

            return result;
        }

        private static bool Belongs(string sample, string family, string type, bool openMetrics)
        {
            if (!openMetrics)
            {
                return sample == family;
            }

            return type switch
            {
                "counter" => sample == family + "_total" || sample == family + "_created",
                "info" => sample == family + "_info",
                _ => sample == family
            };
        }

        private static Sample ParseSample(string line, bool openMetrics)
        {
            var at = 0;

            while (at < line.Length && line[at] != '{' && line[at] != ' ')
            {
                at++;
            }

            var name = line.Substring(0, at);

            if (!MetricName.IsMatch(name))
            {
                throw new FormatException($"Not a metric name: {name}");
            }

            var labels = new Dictionary<string, string>();

            if (at < line.Length && line[at] == '{')
            {
                at++;

                while (line[at] != '}')
                {
                    var equals = line.IndexOf('=', at);
                    var label = line.Substring(at, equals - at);

                    if (!LabelName.IsMatch(label) || label.StartsWith("__", StringComparison.Ordinal))
                    {
                        throw new FormatException($"Not a label name: {label}");
                    }

                    if (line[equals + 1] != '"')
                    {
                        throw new FormatException($"The value of {label} is not quoted.");
                    }

                    var value = new StringBuilder();
                    at = equals + 2;

                    while (line[at] != '"')
                    {
                        if (line[at] == '\\')
                        {
                            value.Append(line[at + 1] switch
                            {
                                '\\' => '\\',
                                '"' => '"',
                                'n' => '\n',
                                _ => throw new FormatException($"Not an escape: \\{line[at + 1]}")
                            });
                            at += 2;
                        }
                        else
                        {
                            value.Append(line[at++]);
                        }
                    }

                    if (!labels.TryAdd(label, value.ToString()))
                    {
                        throw new FormatException($"{label} is given twice.");
                    }

                    at++;

                    if (line[at] == ',')
                    {
                        at++;
                    }
                    else if (line[at] != '}')
                    {
                        throw new FormatException($"Labels not separated by a comma: {line}");
                    }
                }

                at++;
            }

            if (at >= line.Length || line[at] != ' ')
            {
                throw new FormatException($"No value: {line}");
            }

            var rest = line.Substring(at + 1).Split(' ');

            // A value, and maybe a timestamp.
            if (rest.Length > 2 || rest[0].Length == 0)
            {
                throw new FormatException($"Not a value: {line}");
            }

            var number = rest[0] switch
            {
                "+Inf" => double.PositiveInfinity,
                "-Inf" => double.NegativeInfinity,
                "NaN" => double.NaN,
                _ => double.Parse(rest[0], NumberStyles.Float, CultureInfo.InvariantCulture)
            };

            return new Sample { Name = name, Labels = labels, Value = number };
        }

        private static void Unescape(string text, bool openMetrics)
        {
            var escapable = openMetrics ? "\\\"n" : "\\n";

            for (var i = 0; i < text.Length; i++)
            {
                if (openMetrics && text[i] == '"')
                {
                    throw new FormatException($"An unescaped quote in help: {text}");
                }

                if (text[i] == '\\')
                {
                    if (i + 1 >= text.Length || !escapable.Contains(text[i + 1]))
                    {
                        throw new FormatException($"Not an escape in help: {text}");
                    }

                    i++;
                }
                else if (text[i] == '\n')
                {
                    throw new FormatException("A line break in help.");
                }
            }
        }
    }
}

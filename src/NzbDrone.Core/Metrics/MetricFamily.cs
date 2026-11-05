using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Metrics
{
    public enum MetricType
    {
        // Only goes up, except when it starts again from nothing.
        Counter,
        Gauge,

        // Always 1. What it says is in its labels.
        Info
    }

    public class MetricSample
    {
        public MetricSample(IReadOnlyList<KeyValuePair<string, string>> labels, double value)
        {
            Labels = labels;
            Value = value;
        }

        // In the order they are written. A label with no value is left out.
        public IReadOnlyList<KeyValuePair<string, string>> Labels { get; }
        public double Value { get; }
    }

    // One metric and its samples, one for each set of labels. Two samples given the same labels,
    // two friends with the same display name for one, are written as one: added together for a
    // counter, the higher kept for a gauge.
    public class MetricFamily
    {
        private readonly List<MetricSample> _samples = new();

        public MetricFamily(string name, MetricType type, string help, bool sumAlike = true)
        {
            Name = name;
            Type = type;
            Help = help;
            SumAlike = sumAlike;
        }

        // Without the _total of a counter's samples, or the _info of an info's.
        public string Name { get; }
        public MetricType Type { get; }
        public string Help { get; }

        // Whether samples with the same labels are added together, or the higher one kept.
        public bool SumAlike { get; }

        public IReadOnlyList<MetricSample> Samples => _samples;

        public MetricFamily Add(double value, params (string Name, string Value)[] labels)
        {
            var kept = labels
                .Where(l => !string.IsNullOrEmpty(l.Value))
                .Select(l => new KeyValuePair<string, string>(l.Name, l.Value))
                .ToList();

            var index = _samples.FindIndex(s => s.Labels.SequenceEqual(kept));

            if (index < 0)
            {
                _samples.Add(new MetricSample(kept, value));
            }
            else
            {
                var before = _samples[index].Value;
                _samples[index] = new MetricSample(kept, SumAlike ? before + value : Math.Max(before, value));
            }

            return this;
        }
    }
}

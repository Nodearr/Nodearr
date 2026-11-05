using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Metrics;

namespace NzbDrone.Core.Test.Metrics
{
    [TestFixture]
    public class PrometheusTextFixture
    {
        private static MetricFamily[] Families()
        {
            return new[]
            {
                new MetricFamily("nodearr_transfer_bytes", MetricType.Counter, "Bytes \"moved\", \\ and\nmore.")
                    .Add(1500, ("direction", "sent"), ("friend", "Alice \"A\" \\ Smith\nJr"), ("app", "Sonarr"))
                    .Add(0.25, ("direction", "fetched"), ("friend", "Bob"), ("app", null)),
                new MetricFamily("nodearr_upload_queue_length", MetricType.Gauge, "Waiting now.").Add(2),
                new MetricFamily("nodearr_peer_route", MetricType.Gauge, "No samples yet."),
                new MetricFamily("nodearr", MetricType.Info, "Version.").Add(1, ("version", "1.2.3.4"), ("branch", "main"))
            };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void the_output_should_parse(bool openMetrics)
        {
            var parsed = ExpositionParser.Parse(PrometheusText.Write(Families(), openMetrics), openMetrics);

            parsed.Value("nodearr_transfer_bytes_total", ("direction", "sent"), ("friend", "Alice \"A\" \\ Smith\nJr"), ("app", "Sonarr")).Should().Be(1500);
            parsed.Value("nodearr_transfer_bytes_total", ("direction", "fetched"), ("friend", "Bob")).Should().Be(0.25);
            parsed.Value("nodearr_upload_queue_length").Should().Be(2);
            parsed.Value("nodearr_info", ("version", "1.2.3.4"), ("branch", "main")).Should().Be(1);
        }

        [Test]
        public void the_text_format_should_name_each_type_by_its_samples()
        {
            var parsed = ExpositionParser.Parse(PrometheusText.Write(Families(), false), false);

            parsed.Types.Should().Contain("nodearr_transfer_bytes_total", "counter");
            parsed.Types.Should().Contain("nodearr_info", "gauge");
            parsed.Types.Should().Contain("nodearr_peer_route", "gauge");
        }

        [Test]
        public void openmetrics_should_name_each_family_and_end_with_eof()
        {
            var text = PrometheusText.Write(Families(), true);
            var parsed = ExpositionParser.Parse(text, true);

            parsed.Types.Should().Contain("nodearr_transfer_bytes", "counter");
            parsed.Types.Should().Contain("nodearr", "info");
            text.Should().EndWith("\n# EOF\n");
        }

        [Test]
        public void samples_with_the_same_labels_should_be_written_once()
        {
            var counter = new MetricFamily("nodearr_transfers", MetricType.Counter, "Transfers.")
                .Add(2, ("friend", "Sam"))
                .Add(3, ("friend", "Sam"));
            var gauge = new MetricFamily("nodearr_peer_last_seen_timestamp_seconds", MetricType.Gauge, "Seen.", sumAlike: false)
                .Add(100, ("friend", "Sam"))
                .Add(50, ("friend", "Sam"));

            var parsed = ExpositionParser.Parse(PrometheusText.Write(new[] { counter, gauge }, false), false);

            parsed.Value("nodearr_transfers_total", ("friend", "Sam")).Should().Be(5);
            parsed.Value("nodearr_peer_last_seen_timestamp_seconds", ("friend", "Sam")).Should().Be(100);
        }

        [Test]
        public void large_numbers_should_be_written_whole()
        {
            var text = PrometheusText.Write(new[] { new MetricFamily("nodearr_transfer_bytes", MetricType.Counter, "Bytes.").Add(5_000_000_000_000) }, false);

            text.Should().Contain("nodearr_transfer_bytes_total 5000000000000\n");
        }

        [TestCase("application/openmetrics-text;version=1.0.0,text/plain;version=0.0.4;q=0.5,*/*;q=0.1", true)]
        [TestCase("text/plain;version=0.0.4;q=1,*/*;q=0.1", false)]
        [TestCase(null, false)]
        public void openmetrics_should_be_given_when_asked_for(string accept, bool expected)
        {
            PrometheusText.WantsOpenMetrics(accept == null ? new string[0] : new[] { accept }).Should().Be(expected);
        }
    }
}

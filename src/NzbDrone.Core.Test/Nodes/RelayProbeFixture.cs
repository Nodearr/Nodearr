using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Nodes
{
    [TestFixture]
    public class RelayProbeFixture : CoreTest<RelayProbe>
    {
        private TcpListener _listener;
        private string _askedFor;

        [SetUp]
        public void Setup()
        {
            UseRealHttp();
        }

        [TearDown]
        public void TearDown()
        {
            _listener?.Stop();
        }

        // A server on this machine that answers one request with this status, as a relay would
        // with 200 and anything else would with whatever it says to /ping.
        private Uri ServeOnce(int status)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);

                _askedFor = await reader.ReadLineAsync();

                while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                {
                }

                var answer = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Status\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

                await stream.WriteAsync(answer);
            });

            return new Uri($"http://127.0.0.1:{port}/some/path");
        }

        [Test]
        public void a_relay_should_be_asked_for_ping_at_the_root_of_its_host_and_pass()
        {
            var relay = ServeOnce(200);

            Subject.Check(relay).Should().BeNull();
            _askedFor.Should().StartWith("GET /ping ");
        }

        [Test]
        public void something_that_answers_but_not_as_a_relay_should_be_refused_with_what_it_said()
        {
            var relay = ServeOnce(404);

            Subject.Check(relay).Should().Be($"{relay.Authority} answered, but not as a relay: /ping gave HTTP 404");
        }

        [Test]
        public void an_address_where_nothing_answers_should_be_refused()
        {
            // A port that was free a moment ago, and is closed again.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            Subject.Check(new Uri($"http://127.0.0.1:{port}")).Should().StartWith($"No relay answered at 127.0.0.1:{port}: ");
        }
    }
}

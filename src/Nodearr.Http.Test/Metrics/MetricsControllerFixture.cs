using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Nodearr.Http.Authentication;
using Nodearr.Http.Metrics;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Metrics;

namespace Nodearr.Http.Test.Metrics
{
    // /metrics as a scrape meets it: through the same authentication and authorization Nodearr
    // runs with (AddAppAuthentication, AddAppAuthorization), with no session cookie.
    [TestFixture]
    public class MetricsControllerFixture
    {
        private const string ApiKey = "0123456789abcdef0123456789abcdef";

        private IHost _host;
        private HttpClient _client;

        [SetUp]
        public async Task Setup()
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.ApiKey).Returns(ApiKey);
            config.SetupGet(c => c.InstanceName).Returns("Nodearr");
            config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Forms);
            config.SetupGet(c => c.AuthenticationRequired).Returns(AuthenticationRequiredType.Enabled);

            var metrics = new Mock<IMetricsService>();
            metrics.Setup(m => m.Collect()).Returns(new List<MetricFamily>
            {
                new MetricFamily("nodearr_upload_queue_length", MetricType.Gauge, "Waiting now.").Add(3)
            });

            _host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(config.Object);
                        services.AddSingleton(metrics.Object);
                        services.AddDataProtection();
                        services.AddRouting();
                        services.AddControllers().ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Add(new AssemblyPart(typeof(MetricsController).Assembly));
                            parts.FeatureProviders.Remove(parts.FeatureProviders.OfType<ControllerFeatureProvider>().First());
                            parts.FeatureProviders.Add(new OnlyMetrics());
                        });
                        services.AddAppAuthorization();
                        services.AddAppAuthentication();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapControllers());
                    }))
                .StartAsync();

            _client = _host.GetTestClient();
        }

        [TearDown]
        public async Task TearDown()
        {
            _client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }

        [Test]
        public async Task a_scrape_without_the_api_key_should_be_refused()
        {
            var response = await _client.GetAsync("/metrics");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().NotContain("nodearr_");
        }

        [Test]
        public async Task a_scrape_with_the_wrong_api_key_should_be_refused()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
            request.Headers.Add("X-Api-Key", "not-the-key");

            (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.GetAsync("/metrics?apikey=not-the-key")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task the_api_key_as_a_header_should_be_enough()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType.ToString().Should().Be(PrometheusText.TextContentType);
            (await response.Content.ReadAsStringAsync()).Should().Contain("nodearr_upload_queue_length 3\n");
        }

        // What a scrape job's params: apikey sends.
        [Test]
        public async Task the_api_key_as_a_query_parameter_should_be_enough()
        {
            (await _client.GetAsync($"/metrics?apikey={ApiKey}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // What a scrape job's authorization: credentials: sends.
        [Test]
        public async Task the_api_key_as_a_bearer_token_should_be_enough()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Prometheus asks for OpenMetrics first.
        [Test]
        public async Task openmetrics_should_be_given_when_asked_for()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
            request.Headers.Add("X-Api-Key", ApiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/openmetrics-text;version=1.0.0,application/openmetrics-text;version=0.0.1;q=0.75,text/plain;version=0.0.4;q=0.5,*/*;q=0.1");

            var response = await _client.SendAsync(request);

            response.Content.Headers.ContentType.ToString().Should().Be(PrometheusText.OpenMetricsContentType);
            (await response.Content.ReadAsStringAsync()).Should().EndWith("# EOF\n");
        }

        private class OnlyMetrics : ControllerFeatureProvider
        {
            protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(MetricsController);
        }
    }
}

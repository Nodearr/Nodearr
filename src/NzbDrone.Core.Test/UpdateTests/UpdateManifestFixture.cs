using System;
using System.IO;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class UpdateManifestFixture
    {
        private const string Sha = "fef89ed2d269aa1fbeb8b331a5a8437c585c501953e14da2f7eea17147e25721";

        private static byte[] Json(string packages = null, string extra = "")
        {
            packages ??= $"{{ \"win-x64\": {{ \"file\": \"Nodearr.0.10.0.win-x64.zip\", \"url\": \"https://github.com/Nodearr/Nodearr/releases/download/v0.10.0/Nodearr.0.10.0.win-x64.zip\", \"size\": 10, \"sha256\": \"{Sha}\" }} }}";

            return Encoding.UTF8.GetBytes($"{{ \"format\": 1, \"branch\": \"main\", \"version\": \"0.10.0\", \"released\": \"2026-11-10T12:00:00Z\"{extra}, \"packages\": {packages} }}");
        }

        [Test]
        public void should_read_what_publish_update_manifest_writes()
        {
            var manifest = UpdateManifest.Parse(File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Update", "main.json")));

            manifest.Version.ToString().Should().Be("0.8.1");
            manifest.Branch.Should().Be("main");
            manifest.Changelog.Should().Be("https://nodearr.com/changelog/#081-pre-release");
            manifest.Summary.Should().StartWith("Downloads recover");
            manifest.Changes.Should().ContainSingle().Which.Heading.Should().Be("Fixed");
            manifest.Image.Should().Be("ghcr.io/nodearr/nodearr:0.8.1");
            manifest.Rollout.Should().Be(100);
            manifest.Packages.Keys.Should().BeEquivalentTo("win-x64", "linux-musl-x64");
            manifest.Packages["win-x64"].Size.Should().Be(4000);
        }

        [Test]
        public void should_take_every_node_when_no_rollout_is_given()
        {
            UpdateManifest.Parse(Json()).Rollout.Should().Be(100);
        }

        [Test]
        public void should_read_the_released_time_as_utc()
        {
            UpdateManifest.Parse(Json()).Released.Should().Be(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
        }

        [TestCase(", \"rollout\": 0")]
        [TestCase(", \"rollout\": 101")]
        [TestCase(", \"changelog\": \"http://nodearr.com/changelog/\"")]
        public void should_refuse_a_value_out_of_bounds(string extra)
        {
            Assert.Throws<FormatException>(() => UpdateManifest.Parse(Json(extra: extra)));
        }

        [TestCase("{ \"win-x64\": { \"file\": \"a.zip\", \"url\": \"http://example.com/a.zip\", \"size\": 1, \"sha256\": \"" + Sha + "\" } }")]
        [TestCase("{ \"win-x64\": { \"file\": \"a.zip\", \"url\": \"https://example.com/a.zip\", \"size\": 0, \"sha256\": \"" + Sha + "\" } }")]
        [TestCase("{ \"win-x64\": { \"file\": \"a.zip\", \"url\": \"https://example.com/a.zip\", \"size\": 1, \"sha256\": \"abc\" } }")]
        [TestCase("{ \"win-x64\": { \"file\": \"../a.zip\", \"url\": \"https://example.com/a.zip\", \"size\": 1, \"sha256\": \"" + Sha + "\" } }")]
        public void should_refuse_a_package_that_is_not_described_properly(string packages)
        {
            Assert.Throws<FormatException>(() => UpdateManifest.Parse(Json(packages)));
        }

        [Test]
        public void should_allow_http_only_on_this_machine_for_testing()
        {
            var manifest = UpdateManifest.Parse(Json("{ \"win-x64\": { \"file\": \"a.zip\", \"url\": \"http://127.0.0.1:8080/a.zip\", \"size\": 1, \"sha256\": \"" + Sha + "\" } }"));

            manifest.Packages["win-x64"].Url.Should().Be("http://127.0.0.1:8080/a.zip");
        }

        [TestCase("{ \"format\": 2, \"branch\": \"main\", \"version\": \"0.10.0\", \"released\": \"2026-11-10T12:00:00Z\", \"packages\": {} }")]
        [TestCase("{ \"format\": 1, \"branch\": \"main\", \"version\": \"latest\", \"released\": \"2026-11-10T12:00:00Z\", \"packages\": {} }")]
        [TestCase("{ \"format\": 1, \"branch\": \"main\", \"version\": \"0.10.0\", \"released\": \"2026-11-10T12:00:00Z\" }")]
        [TestCase("[]")]
        [TestCase("not json")]
        public void should_refuse_a_manifest_it_does_not_understand(string json)
        {
            Assert.Throws<FormatException>(() => UpdateManifest.Parse(Encoding.UTF8.GetBytes(json)));
        }
    }
}

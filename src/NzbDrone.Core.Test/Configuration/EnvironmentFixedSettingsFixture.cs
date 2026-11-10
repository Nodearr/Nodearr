using FluentAssertions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Test.Configuration
{
    // Which general settings the settings page must show as fixed.
    [TestFixture]
    public class EnvironmentFixedSettingsFixture
    {
        private static EnvironmentFixedSettings Given(ServerOptions server = null, AuthOptions auth = null, AppOptions app = null, LogOptions log = null, UpdateOptions update = null, NetworkOptions network = null, UsageOptions usage = null)
        {
            return new EnvironmentFixedSettings(
                Options.Create(server ?? new ServerOptions()),
                Options.Create(auth ?? new AuthOptions()),
                Options.Create(app ?? new AppOptions()),
                Options.Create(log ?? new LogOptions()),
                Options.Create(update ?? new UpdateOptions()),
                Options.Create(network ?? new NetworkOptions()),
                Options.Create(usage ?? new UsageOptions()));
        }

        [Test]
        public void nothing_should_be_fixed_when_nothing_is_given_in_the_environment()
        {
            Given().General().Should().BeEmpty();
        }

        [Test]
        public void what_a_container_is_given_should_be_fixed_and_nothing_else()
        {
            // What the Docker image sets.
            Given(new ServerOptions { Port = 6633, BindAddress = "*" }).General().Should().BeEquivalentTo("port", "bindAddress");
        }

        [Test]
        public void a_key_and_a_way_of_signing_in_given_in_the_environment_should_be_fixed()
        {
            Given(auth: new AuthOptions { ApiKey = "0123456789abcdef0123456789abcdef", Method = "External" })
                .General().Should().BeEquivalentTo("apiKey", "authenticationMethod");
        }

        [Test]
        public void a_value_that_happens_to_be_the_default_is_still_fixed_when_it_was_given()
        {
            Given(new ServerOptions { EnableSsl = false, UrlBase = string.Empty }, log: new LogOptions { Level = "info" })
                .General().Should().BeEquivalentTo("enableSsl", "urlBase", "logLevel");
        }

        [Test]
        public void the_update_settings_and_the_switch_for_nodearr_com_given_in_the_environment_should_be_fixed()
        {
            Given(update: new UpdateOptions { Check = false, Automatically = true }, network: new NetworkOptions { ContactNodearr = false })
                .General().Should().BeEquivalentTo("updateCheck", "updateAutomatically", "contactNodearr");
        }

        [Test]
        public void a_manifest_address_and_key_for_testing_are_not_settings_the_page_shows()
        {
            Given(update: new UpdateOptions { ManifestUrl = "http://127.0.0.1:1/", PublicKey = "ssh-rsa AAAA" }).General().Should().BeEmpty();
        }

        [Test]
        public void sending_usage_data_given_in_the_environment_should_be_fixed()
        {
            Given(usage: new UsageOptions { Send = false }).General().Should().BeEquivalentTo("usageSend");
        }

        [Test]
        public void a_usage_address_for_testing_is_not_a_setting_the_page_shows()
        {
            Given(usage: new UsageOptions { Url = "http://127.0.0.1:1/v1/report" }).General().Should().BeEmpty();
        }

        [Test]
        public void every_name_should_be_one_the_settings_page_knows()
        {
            var all = Given(
                new ServerOptions { BindAddress = "*", Port = 1, UrlBase = "/n", EnableSsl = true, SslPort = 2, SslCertPath = "c", SslKeyPath = "k", SslCertPassword = "p" },
                new AuthOptions { ApiKey = "k", Method = "Forms", Required = "Enabled" },
                new AppOptions { InstanceName = "Nodearr", LaunchBrowser = false },
                new LogOptions { Level = "debug", SizeLimit = 1 },
                new UpdateOptions { Check = true, Automatically = false },
                new NetworkOptions { ContactNodearr = false },
                new UsageOptions { Send = false }).General();

            all.Should().OnlyHaveUniqueItems();
            all.Should().HaveCount(19);
            all.Should().OnlyContain(name => char.IsLower(name[0]));
        }
    }
}

using System;
using System.IO;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Options;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class UpdatePackageProviderFixture : CoreTest<UpdatePackageProvider>
    {
        private byte[] _manifest;
        private byte[] _signature;
        private UpdateOptions _options;

        private static string Fixture(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Update", name);

        [SetUp]
        public void Setup()
        {
            _manifest = File.ReadAllBytes(Fixture("main.json"));
            _signature = File.ReadAllBytes(Fixture("main.json.sig"));

            // The fixture is signed by a key made for these tests, which only the environment can
            // make Nodearr trust.
            _options = new UpdateOptions { PublicKey = File.ReadAllText(Fixture("test-key.pub")) };
            Mocker.GetMock<IOptions<UpdateOptions>>().Setup(o => o.Value).Returns(() => _options);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), r.Url.FullUri.EndsWith(".sig") ? _signature : _manifest, HttpStatusCode.OK));

            Subject.Pause = _ => { };
        }

        [Test]
        public void should_fetch_main_json_and_its_signature_from_updates_nodearr_com()
        {
            _options.PublicKey = null;

            Subject.Fetch("main");

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == "https://updates.nodearr.com/v1/main.json")), Times.AtLeastOnce());
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == "https://updates.nodearr.com/v1/main.json.sig")), Times.AtLeastOnce());

            // The fixture is not signed by the built-in key.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_send_no_identifier_no_query_and_no_cookies()
        {
            var sent = new System.Collections.Generic.List<HttpRequest>();
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(sent.Add)
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), r.Url.FullUri.EndsWith(".sig") ? _signature : _manifest, HttpStatusCode.OK));

            Subject.Fetch("main");

            sent.Should().HaveCount(2);
            sent.Should().OnlyContain(r =>
                string.IsNullOrEmpty(r.Url.Query) &&
                r.UseSimplifiedUserAgent &&
                !r.StoreRequestCookie &&
                !r.StoreResponseCookie &&
                r.Cookies.Count == 0 &&
                r.ContentData == null);
        }

        [Test]
        public void should_use_the_manifest_folder_given_in_the_environment()
        {
            _options.ManifestUrl = "http://127.0.0.1:18989/v1/";

            Subject.Fetch("main");

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == "http://127.0.0.1:18989/v1/main.json")), Times.Once());
        }

        [Test]
        public void should_accept_a_manifest_signed_by_the_key_it_trusts()
        {
            var result = Subject.Fetch("main");

            result.Outcome.Should().Be(UpdateManifestOutcome.Verified);
            result.Manifest.Version.ToString().Should().Be("0.8.1");
        }

        [Test]
        public void should_refuse_a_manifest_signed_by_any_other_key()
        {
            // The built-in key did not sign the test fixture.
            _options.PublicKey = null;

            var result = Subject.Fetch("main");

            result.Outcome.Should().Be(UpdateManifestOutcome.BadSignature);
            result.Manifest.Should().BeNull();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_refuse_a_tampered_manifest_after_fetching_it_again()
        {
            _manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(_manifest).Replace("\"rollout\": 100", "\"rollout\": 99"));

            var result = Subject.Fetch("main");

            result.Outcome.Should().Be(UpdateManifestOutcome.BadSignature);
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.EndsWith("main.json"))), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_accept_it_when_a_second_fetch_finds_the_signature_that_goes_with_it()
        {
            var good = _signature;
            _signature = Encoding.UTF8.GetBytes("-----BEGIN SSH SIGNATURE-----\nAAAA\n-----END SSH SIGNATURE-----\n");
            var paused = TimeSpan.Zero;
            Subject.Pause = t =>
            {
                paused = t;
                _signature = good;
            };

            var result = Subject.Fetch("main");

            result.Outcome.Should().Be(UpdateManifestOutcome.Verified);
            paused.Should().Be(UpdatePackageProvider.RetryPause);
        }

        [Test]
        public void should_say_nothing_but_unavailable_when_it_cannot_be_fetched()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Throws(new WebException("No such host is known"));

            var result = Subject.Fetch("main");

            result.Outcome.Should().Be(UpdateManifestOutcome.Unavailable);
            ExceptionVerification.AssertNoUnexpectedLogs();
        }
    }
}

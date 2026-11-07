using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Usage;
using NzbDrone.Core.Usage.Commands;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Usage
{
    [TestFixture]
    public class UsageReportServiceFixture : CoreTest<UsageReportService>
    {
        private List<HttpRequest> _requests;
        private HttpStatusCode _status;
        private UsageOptions _options;

        [SetUp]
        public void Setup()
        {
            _requests = new List<HttpRequest>();
            _status = HttpStatusCode.NoContent;
            _options = new UsageOptions();

            Mocker.GetMock<IOptions<UsageOptions>>().Setup(o => o.Value).Returns(() => _options);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ContactNodearr).Returns(true);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.UsageSend).Returns(true);
            Mocker.GetMock<IUsageReportBuilder>().Setup(b => b.Build(It.IsAny<DateTime>())).Returns(new UsageReport { Id = "6f1c2b9e-3d4a-4e5f-8a7b-0c1d2e3f4a5b", Language = "en" });

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => _requests.Add(r))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), Array.Empty<byte>(), _status));
        }

        [Test]
        public void a_report_should_be_one_post_of_the_report_to_usage_nodearr_com()
        {
            Subject.Send().Should().Be(UsageReportOutcome.Sent);

            _requests.Should().HaveCount(1);

            var request = _requests[0];
            request.Method.Should().Be(HttpMethod.Post);
            request.Url.FullUri.Should().Be("https://usage.nodearr.com/v1/report");
            request.Url.Query.Should().BeNullOrEmpty();
            request.Headers.ContentType.Should().Be("application/json");
            request.UseSimplifiedUserAgent.Should().BeTrue();
            request.StoreRequestCookie.Should().BeFalse();
            request.Cookies.Should().BeEmpty();
            Encoding.UTF8.GetString(request.ContentData).Should().Be(UsageReportBuilder.ToJson(Mocker.Resolve<IUsageReportBuilder>().Build(DateTime.UtcNow)));
        }

        [Test]
        public void the_report_sent_should_be_the_one_show_report_shows()
        {
            var preview = Subject.Preview();

            Subject.Send();

            Encoding.UTF8.GetString(_requests[0].ContentData).Should().Be(preview.Report);
            preview.Sending.Should().BeTrue();
        }

        [Test]
        public void nothing_should_be_sent_with_send_anonymous_usage_data_off()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.UsageSend).Returns(false);

            Subject.Send().Should().Be(UsageReportOutcome.Off);

            _requests.Should().BeEmpty();
            Mocker.GetMock<IUsageReportBuilder>().Verify(b => b.Build(It.IsAny<DateTime>()), Times.Never());
        }

        [Test]
        public void contact_nodearr_com_off_should_win_over_send_anonymous_usage_data()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ContactNodearr).Returns(false);

            Subject.Send().Should().Be(UsageReportOutcome.Off);

            _requests.Should().BeEmpty();
            Subject.Preview().Sending.Should().BeFalse();
        }

        [Test]
        public void show_report_should_show_the_report_with_sending_off_too()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.UsageSend).Returns(false);

            var preview = Subject.Preview();

            preview.Sending.Should().BeFalse();
            preview.Report.Should().Contain("6f1c2b9e-3d4a-4e5f-8a7b-0c1d2e3f4a5b");
            _requests.Should().BeEmpty();
        }

        [TestCase(HttpStatusCode.BadRequest)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public void a_refused_report_should_fail_quietly_and_not_be_tried_again(HttpStatusCode status)
        {
            _status = status;

            Subject.Send().Should().Be(UsageReportOutcome.Failed);

            _requests.Should().HaveCount(1);
        }

        [Test]
        public void a_host_that_cannot_be_reached_should_fail_quietly()
        {
            Mocker.GetMock<IHttpClient>().Setup(c => c.Execute(It.IsAny<HttpRequest>())).Throws(new WebException("No such host is known. (usage.nodearr.com:443)"));

            Subject.Execute(new SendUsageReportCommand());

            ExceptionVerification.ExpectedErrors(0);
            ExceptionVerification.ExpectedWarns(0);
        }

        [Test]
        public void the_address_can_be_changed_for_testing_in_the_environment()
        {
            _options.Url = "http://127.0.0.1:18080/v1/report";

            Subject.Send();

            _requests[0].Url.FullUri.Should().Be("http://127.0.0.1:18080/v1/report");
        }
    }
}

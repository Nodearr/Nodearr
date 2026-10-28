using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nodearr.Http.Newznab;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Test.Common;

namespace Nodearr.Http.Test.Newznab
{
    [TestFixture]
    public class NewznabControllerFixture : TestBase<NewznabController>
    {
        [SetUp]
        public void Setup()
        {
            Subject.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            Subject.Request.Host = new HostString("localhost", 6633);
            Subject.Request.Scheme = "http";
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ApiKey).Returns("key");
            Mocker.GetMock<ICandidateService>().Setup(c => c.Search(It.IsAny<CandidateQuery>())).Returns(new CandidateResult());
        }

        [TestCase("search", "", 12)]
        [TestCase("search", "?q=Pingu", null)]
        [TestCase("search", "?tvdbid=75669", null)]
        [TestCase("search", "?season=1&ep=2", null)]
        [TestCase("tvsearch", "", 12)]
        [TestCase("movie", "", 12)]
        [TestCase("music", "", 12)]
        [TestCase("tvsearch", "?tvdbid=75669&season=1&ep=1", null)]
        [TestCase("movie", "?tmdbid=10331", null)]
        [TestCase("music", "?artist=Slint&album=Spiderland", null)]
        public void only_queryless_app_browse_should_use_the_filtered_feed(string function, string query, int? expectedApp)
        {
            Subject.Request.QueryString = new QueryString(query);
            Subject.ApplicationApi(12, function, "key");
            Mocker.GetMock<ICandidateService>().Verify(c => c.Search(It.Is<CandidateQuery>(q => q.RssApplicationId == expectedApp)), Times.Once());
        }

        [Test]
        public void legacy_browse_should_keep_its_unfiltered_behavior()
        {
            Subject.Api("search", "key");
            Mocker.GetMock<ICandidateService>().Verify(c => c.Search(It.Is<CandidateQuery>(q => q.RssApplicationId == null)), Times.Once());
        }

        [Test]
        public void an_app_url_should_still_require_the_api_key()
        {
            Subject.ApplicationApi(12, "search", "wrong");
            Mocker.GetMock<ICandidateService>().Verify(c => c.Search(It.IsAny<CandidateQuery>()), Times.Never());
        }
    }
}

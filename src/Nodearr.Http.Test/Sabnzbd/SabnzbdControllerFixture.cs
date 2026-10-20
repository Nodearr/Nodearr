using System.Collections.Generic;
using System.Linq;
using Moq;
using Nodearr.Http.Sabnzbd;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Lidarr;
using NzbDrone.Core.Applications.Whisparr;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Transfers;
using NzbDrone.Test.Common;

namespace Nodearr.Http.Test.Sabnzbd
{
    [TestFixture]
    public class SabnzbdControllerFixture : TestBase<SabnzbdController>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.ApiKey).Returns("key");
            Mocker.GetMock<IApplicationFactory>()
                .Setup(f => f.All())
                .Returns(new List<ApplicationDefinition>
                {
                    new() { Settings = new LidarrSettings { DownloadCategory = "music" } },
                    new() { Settings = new WhisparrV3Settings() }
                });
        }

        // Sonarr warned that the folder for its category did not exist until the first transfer
        // for it arrived. It checks when it reads the config, so the folders are made then.
        [Test]
        public void reading_the_config_should_make_the_folder_of_every_category_it_lists()
        {
            Subject.Api("get_config", null, null, "key");

            var expected = new[] { "movies", "music", "tv", "whisparr" };

            Mocker.GetMock<ITransferFolders>()
                .Verify(f => f.EnsureCategoryFolders(It.Is<IEnumerable<string>>(c => c.OrderBy(x => x).SequenceEqual(expected))), Times.Once());
        }

        [Test]
        public void nothing_should_be_made_for_a_caller_without_the_api_key()
        {
            Subject.Api("get_config", null, null, "wrong");

            Mocker.GetMock<ITransferFolders>().Verify(f => f.EnsureCategoryFolders(It.IsAny<IEnumerable<string>>()), Times.Never());
        }
    }
}

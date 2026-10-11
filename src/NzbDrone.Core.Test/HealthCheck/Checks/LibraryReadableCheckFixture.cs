using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class LibraryReadableCheckFixture : CoreTest<LibraryReadableCheck>
    {
        private Dictionary<string, object> _tokens;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Callback<string, Dictionary<string, object>>((_, tokens) => _tokens = tokens)
                  .Returns("Some Warning Message");

            Mocker.GetMock<IApplicationFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<ApplicationDefinition> { new() { Id = 1, Name = "Sonarr" } });

            Mocker.GetMock<ICatalogMovieFileRepository>()
                  .Setup(r => r.ForApplication(It.IsAny<int>()))
                  .Returns(new List<CatalogMovieFile>());
        }

        private void GivenEpisodeFiles(int count, Func<int, bool> isReadable, Func<int, bool> isMissing = null)
        {
            Mocker.GetMock<ICatalogEpisodeFileRepository>()
                  .Setup(r => r.ForApplication(1))
                  .Returns(Enumerable.Range(0, count).Select(i => new CatalogEpisodeFile
                  {
                      ApplicationId = 1,
                      ArrId = i,
                      Path = $"/tv/file-{i}.mkv",
                      MissingSince = isMissing != null && isMissing(i) ? DateTime.UtcNow : null
                  }).ToList());

            Mocker.GetMock<ICatalogFileLocator>()
                  .Setup(l => l.Locate(1, It.IsAny<string>()))
                  .Returns<int, string>((_, path) => new LocatedFile
                  {
                      LocalPath = path,
                      Readable = isReadable(int.Parse(path.Split('-', '.')[1]))
                  });
        }

        [Test]
        public void should_be_ok_when_every_file_can_be_read()
        {
            GivenEpisodeFiles(5, _ => true);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_be_ok_when_there_is_nothing_in_the_catalog()
        {
            GivenEpisodeFiles(0, _ => false);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_warn_and_name_a_file_when_the_library_cannot_be_read()
        {
            GivenEpisodeFiles(5, _ => false);

            Subject.Check().ShouldBeWarning();

            _tokens["applicationName"].Should().Be("Sonarr");
            _tokens["unreadable"].Should().Be(5);
            _tokens["checked"].Should().Be(5);
            _tokens["path"].Should().Be("/tv/file-0.mkv");
        }

        [Test]
        public void should_warn_when_only_some_files_cannot_be_read()
        {
            GivenEpisodeFiles(10, i => i != 7);

            Subject.Check().ShouldBeWarning();

            _tokens["unreadable"].Should().Be(1);
            _tokens["path"].Should().Be("/tv/file-7.mkv");
        }

        [Test]
        public void should_not_count_files_the_arr_app_no_longer_reports()
        {
            GivenEpisodeFiles(4, i => i != 2, i => i == 2);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_look_at_a_sample_of_a_large_library_and_not_at_every_file()
        {
            GivenEpisodeFiles(5000, _ => false);

            Subject.Check().ShouldBeWarning();

            _tokens["checked"].Should().Be(100);

            // Once for each file in the sample, and once more to name the example.
            Mocker.GetMock<ICatalogFileLocator>().Verify(l => l.Locate(1, It.IsAny<string>()), Times.Exactly(101));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.HealthCheck;
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

        private static string PathOf(int i) => $"/tv/file-{i}.mkv";

        private void GivenEpisodeFiles(int count, Func<int, CatalogFileAccess> access, Func<int, bool> isMissing = null)
        {
            Mocker.GetMock<ICatalogEpisodeFileRepository>()
                  .Setup(r => r.ForApplication(1))
                  .Returns(Enumerable.Range(0, count).Select(i => new CatalogEpisodeFile
                  {
                      ApplicationId = 1,
                      ArrId = i,
                      Path = PathOf(i),
                      MissingSince = isMissing != null && isMissing(i) ? DateTime.UtcNow : null
                  }).ToList());

            Mocker.GetMock<ICatalogFileLocator>()
                  .Setup(l => l.Locate(1, It.IsAny<string>()))
                  .Returns<int, string>((_, path) => new LocatedFile
                  {
                      LocalPath = path,
                      IsPathHere = true
                  });

            Mocker.GetMock<ICatalogFileLocator>()
                  .Setup(l => l.Open(It.IsAny<LocatedFile>()))
                  .Returns<LocatedFile>(f => access(Index(f.LocalPath)));
        }

        private static int Index(string path) => int.Parse(path.Split('-', '.')[1]);

        [Test]
        public void should_be_ok_when_every_file_can_be_read()
        {
            GivenEpisodeFiles(5, _ => CatalogFileAccess.Readable);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_be_ok_when_there_is_nothing_in_the_catalog()
        {
            GivenEpisodeFiles(0, _ => CatalogFileAccess.Missing);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_say_the_library_is_not_found_when_no_file_is_there()
        {
            GivenEpisodeFiles(5, _ => CatalogFileAccess.Missing);

            Subject.Check().ShouldBeWarning();
            Subject.Check().Reason.Should().Be(HealthCheckReason.LibraryNotFound);

            _tokens["applicationName"].Should().Be("Sonarr");
            _tokens["count"].Should().Be(5);
            _tokens["checked"].Should().Be(5);
            _tokens["path"].Should().Be(PathOf(0));
        }

        // A file transcoded to another name, with the Arr app not yet told, was reported as one
        // Nodearr could not read. Nodearr could read everything there; the file was not there.
        [Test]
        public void should_say_the_arr_app_lists_files_that_are_not_there_when_only_some_are_missing()
        {
            GivenEpisodeFiles(10, i => i == 7 ? CatalogFileAccess.Missing : CatalogFileAccess.Readable);

            Subject.Check().ShouldBeWarning();
            Subject.Check().Reason.Should().Be(HealthCheckReason.LibraryFilesMissing);

            _tokens["count"].Should().Be(1);
            _tokens["path"].Should().Be(PathOf(7));
        }

        [Test]
        public void should_say_nodearr_is_not_allowed_to_open_files_that_are_there()
        {
            GivenEpisodeFiles(10, i => i == 3 ? CatalogFileAccess.NotAllowed : CatalogFileAccess.Readable);

            Subject.Check().ShouldBeWarning();
            Subject.Check().Reason.Should().Be(HealthCheckReason.LibraryNotReadable);

            _tokens["count"].Should().Be(1);
            _tokens["path"].Should().Be(PathOf(3));
        }

        [Test]
        public void should_say_a_whole_library_it_may_not_open_is_not_allowed_rather_than_not_found()
        {
            GivenEpisodeFiles(5, _ => CatalogFileAccess.NotAllowed);

            Subject.Check().Reason.Should().Be(HealthCheckReason.LibraryNotReadable);
        }

        [Test]
        public void should_name_files_it_may_not_open_before_files_that_are_missing()
        {
            GivenEpisodeFiles(10, i => i switch { 2 => CatalogFileAccess.Missing, 5 => CatalogFileAccess.NotAllowed, _ => CatalogFileAccess.Readable });

            Subject.Check().Reason.Should().Be(HealthCheckReason.LibraryNotReadable);

            _tokens["path"].Should().Be(PathOf(5));
        }

        [Test]
        public void should_not_count_files_the_arr_app_no_longer_reports()
        {
            GivenEpisodeFiles(4, i => i == 2 ? CatalogFileAccess.Missing : CatalogFileAccess.Readable, i => i == 2);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_look_at_a_sample_of_a_large_library_and_not_at_every_file()
        {
            GivenEpisodeFiles(5000, _ => CatalogFileAccess.NotAllowed);

            Subject.Check().ShouldBeWarning();

            _tokens["checked"].Should().Be(100);

            Mocker.GetMock<ICatalogFileLocator>().Verify(l => l.Locate(1, It.IsAny<string>()), Times.Exactly(100));
            Mocker.GetMock<ICatalogFileLocator>().Verify(l => l.Open(It.IsAny<LocatedFile>()), Times.Exactly(100));
        }
    }
}

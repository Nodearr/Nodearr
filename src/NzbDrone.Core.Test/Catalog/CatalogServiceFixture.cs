using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Catalog
{
    [TestFixture]
    public class CatalogServiceFixture : DbTest<CatalogService, CatalogSeries>
    {
        private const int ApplicationId = 1;

        private ApplicationCatalog _catalog;
        private Mock<IApplication> _application;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICatalogQualityProfileRepository>(Mocker.Resolve<CatalogQualityProfileRepository>());
            Mocker.SetConstant<ICatalogSeriesRepository>(Mocker.Resolve<CatalogSeriesRepository>());
            Mocker.SetConstant<ICatalogEpisodeRepository>(Mocker.Resolve<CatalogEpisodeRepository>());
            Mocker.SetConstant<ICatalogEpisodeFileRepository>(Mocker.Resolve<CatalogEpisodeFileRepository>());
            Mocker.SetConstant<ICatalogMovieRepository>(Mocker.Resolve<CatalogMovieRepository>());
            Mocker.SetConstant<ICatalogMovieFileRepository>(Mocker.Resolve<CatalogMovieFileRepository>());
            Mocker.SetConstant<ICatalogSyncStatusRepository>(Mocker.Resolve<CatalogSyncStatusRepository>());

            _catalog = GivenCatalog();

            _application = new Mock<IApplication>();
            _application.SetupGet(a => a.Definition).Returns(new ApplicationDefinition { Id = ApplicationId, Name = "Sonarr" });
            _application.Setup(a => a.FetchCatalog()).Returns(() => _catalog);

            Mocker.GetMock<IApplicationFactory>()
                  .Setup(f => f.SyncEnabled(It.IsAny<bool>()))
                  .Returns(new List<IApplication> { _application.Object });
        }

        // One series, with one file that holds episodes 4 and 5 and one file that holds episode 1.
        private static ApplicationCatalog GivenCatalog()
        {
            var catalog = new ApplicationCatalog();

            catalog.Series.Add(new CatalogSeries { ArrId = 10, TvdbId = 75669, Title = "Pingu", Year = 1989 });
            catalog.Episodes.Add(new CatalogEpisode { ArrId = 101, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 501 });
            catalog.Episodes.Add(new CatalogEpisode { ArrId = 104, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 4, ArrEpisodeFileId = 502 });
            catalog.Episodes.Add(new CatalogEpisode { ArrId = 105, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 5, ArrEpisodeFileId = 502 });
            catalog.EpisodeFiles.Add(new CatalogEpisodeFile { ArrId = 501, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/S01E01.mkv", Size = 100, Quality = "WEBDL-720p" });
            catalog.EpisodeFiles.Add(new CatalogEpisodeFile { ArrId = 502, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/S01E04E05.mkv", Size = 200, Quality = "WEBDL-720p" });

            return catalog;
        }

        private List<CatalogEpisodeFile> EpisodeFiles => Mocker.Resolve<ICatalogEpisodeFileRepository>().ForApplication(ApplicationId);

        [Test]
        public void should_import_what_the_application_reports()
        {
            Subject.Refresh(null, false);

            Mocker.Resolve<ICatalogSeriesRepository>().ForApplication(ApplicationId).Should().ContainSingle(s => s.Title == "Pingu" && s.TvdbId == 75669);
            Mocker.Resolve<ICatalogEpisodeRepository>().ForApplication(ApplicationId).Should().HaveCount(3);
            EpisodeFiles.Should().HaveCount(2);
            EpisodeFiles.Should().OnlyContain(f => f.MissingSince == null);
        }

        [Test]
        public void should_keep_both_episodes_of_a_multi_episode_file()
        {
            Subject.Refresh(null, false);

            Subject.GetEpisodesForFiles(ApplicationId, new[] { 502 }).Select(e => e.EpisodeNumber).Should().BeEquivalentTo(new[] { 4, 5 });
        }

        [Test]
        public void should_update_a_changed_item_in_place()
        {
            Subject.Refresh(null, false);
            var id = EpisodeFiles.Single(f => f.ArrId == 501).Id;

            _catalog = GivenCatalog();
            _catalog.EpisodeFiles.Single(f => f.ArrId == 501).Quality = "Bluray-1080p";
            Subject.Refresh(null, false);

            var updated = EpisodeFiles.Single(f => f.ArrId == 501);
            updated.Id.Should().Be(id);
            updated.Quality.Should().Be("Bluray-1080p");
            EpisodeFiles.Should().HaveCount(2);
        }

        [Test]
        public void should_mark_an_item_missing_when_it_is_no_longer_reported()
        {
            Subject.Refresh(null, false);

            _catalog = GivenCatalog();
            _catalog.EpisodeFiles.RemoveAll(f => f.ArrId == 501);
            Subject.Refresh(null, false);

            EpisodeFiles.Should().HaveCount(2);
            EpisodeFiles.Single(f => f.ArrId == 501).MissingSince.Should().NotBeNull();
            EpisodeFiles.Single(f => f.ArrId == 502).MissingSince.Should().BeNull();
        }

        [Test]
        public void should_clear_missing_when_the_item_comes_back()
        {
            Subject.Refresh(null, false);

            _catalog = GivenCatalog();
            _catalog.EpisodeFiles.RemoveAll(f => f.ArrId == 501);
            Subject.Refresh(null, false);

            _catalog = GivenCatalog();
            Subject.Refresh(null, false);

            EpisodeFiles.Should().OnlyContain(f => f.MissingSince == null);
        }

        [Test]
        public void should_purge_items_already_missing_when_asked()
        {
            Subject.Refresh(null, false);

            _catalog = GivenCatalog();
            _catalog.EpisodeFiles.RemoveAll(f => f.ArrId == 501);
            Subject.Refresh(null, false);

            _catalog = GivenCatalog();
            _catalog.EpisodeFiles.RemoveAll(f => f.ArrId == 501);
            Subject.Refresh(null, true);

            EpisodeFiles.Should().ContainSingle(f => f.ArrId == 502);
        }

        [Test]
        public void should_not_purge_an_item_in_the_refresh_that_first_misses_it()
        {
            Subject.Refresh(null, false);

            _catalog = GivenCatalog();
            _catalog.EpisodeFiles.RemoveAll(f => f.ArrId == 501);
            Subject.Refresh(null, true);

            EpisodeFiles.Should().HaveCount(2);
        }

        [Test]
        public void should_change_nothing_when_the_application_cannot_be_read()
        {
            Subject.Refresh(null, false);

            _application.Setup(a => a.FetchCatalog()).Throws(new InvalidOperationException("Sonarr is down"));
            Subject.Refresh(null, true);

            EpisodeFiles.Should().HaveCount(2);
            EpisodeFiles.Should().OnlyContain(f => f.MissingSince == null);
            Mocker.Resolve<ICatalogSyncStatusRepository>().ForApplication(ApplicationId).LastSyncError.Should().Be("Sonarr is down");

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_only_refresh_the_requested_application()
        {
            Subject.Refresh(ApplicationId + 1, false);

            _application.Verify(a => a.FetchCatalog(), Times.Never());
        }

        [Test]
        public void should_remove_the_catalog_of_a_deleted_application()
        {
            Subject.Refresh(null, false);

            Subject.Handle(new ProviderDeletedEvent<IApplication>(ApplicationId));

            EpisodeFiles.Should().BeEmpty();
            Mocker.Resolve<ICatalogSeriesRepository>().ForApplication(ApplicationId).Should().BeEmpty();
            Mocker.Resolve<ICatalogSyncStatusRepository>().ForApplication(ApplicationId).Should().BeNull();
        }
    }
}

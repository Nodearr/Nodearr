using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Events;
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
            Mocker.SetConstant<ICatalogArtistRepository>(Mocker.Resolve<CatalogArtistRepository>());
            Mocker.SetConstant<ICatalogAlbumRepository>(Mocker.Resolve<CatalogAlbumRepository>());
            Mocker.SetConstant<ICatalogTrackFileRepository>(Mocker.Resolve<CatalogTrackFileRepository>());
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

        // A second series, Bluey, beside Pingu, so that reading one title again can be seen to
        // leave the other alone.
        private void GivenTwoSeriesImported()
        {
            _catalog.Series.Add(new CatalogSeries { ArrId = 11, TvdbId = 353546, Title = "Bluey", Year = 2018 });
            _catalog.Episodes.Add(new CatalogEpisode { ArrId = 201, ArrSeriesId = 11, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 601 });
            _catalog.EpisodeFiles.Add(new CatalogEpisodeFile { ArrId = 601, ArrSeriesId = 11, SeasonNumber = 1, Path = "/tv/Bluey/S01E01.mkv", Size = 300 });
            Subject.Refresh(null, false);
        }

        // Pingu as Sonarr has it after the webhook: episode 1's file deleted, episode 6 imported.
        private static ApplicationCatalog PinguNow()
        {
            var pingu = new ApplicationCatalog();

            pingu.Series.Add(new CatalogSeries { ArrId = 10, TvdbId = 75669, Title = "Pingu", Year = 1989 });
            pingu.Episodes.Add(new CatalogEpisode { ArrId = 101, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1 });
            pingu.Episodes.Add(new CatalogEpisode { ArrId = 104, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 4, ArrEpisodeFileId = 502 });
            pingu.Episodes.Add(new CatalogEpisode { ArrId = 105, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 5, ArrEpisodeFileId = 502 });
            pingu.Episodes.Add(new CatalogEpisode { ArrId = 106, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 6, ArrEpisodeFileId = 503 });
            pingu.EpisodeFiles.Add(new CatalogEpisodeFile { ArrId = 502, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/S01E04E05.mkv", Size = 200, Quality = "WEBDL-720p" });
            pingu.EpisodeFiles.Add(new CatalogEpisodeFile { ArrId = 503, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/S01E06.mkv", Size = 400, Quality = "WEBDL-720p" });

            return pingu;
        }

        [Test]
        public void reading_one_series_again_should_take_in_its_changes_and_leave_the_rest_alone()
        {
            GivenTwoSeriesImported();
            _application.Setup(a => a.FetchTitle(10)).Returns(PinguNow());

            Subject.RefreshTitle(ApplicationId, 10);

            EpisodeFiles.Single(f => f.ArrId == 503).MissingSince.Should().BeNull();
            EpisodeFiles.Single(f => f.ArrId == 501).MissingSince.Should().NotBeNull();
            EpisodeFiles.Single(f => f.ArrId == 601).MissingSince.Should().BeNull();
            Mocker.Resolve<ICatalogSeriesRepository>().ForApplication(ApplicationId).Should().OnlyContain(s => s.MissingSince == null);
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<CatalogRefreshedEvent>(c => c.ApplicationId == ApplicationId)), Times.Exactly(2));
        }

        [Test]
        public void a_series_the_arr_app_no_longer_has_should_be_marked_missing_with_everything_in_it()
        {
            GivenTwoSeriesImported();
            _application.Setup(a => a.FetchTitle(10)).Returns(new ApplicationCatalog());

            Subject.RefreshTitle(ApplicationId, 10);

            Mocker.Resolve<ICatalogSeriesRepository>().ForApplication(ApplicationId).Single(s => s.ArrId == 10).MissingSince.Should().NotBeNull();
            Mocker.Resolve<ICatalogEpisodeRepository>().ForSeries(ApplicationId, 10).Should().OnlyContain(e => e.MissingSince != null);
            EpisodeFiles.Where(f => f.ArrSeriesId == 10).Should().OnlyContain(f => f.MissingSince != null);
            EpisodeFiles.Single(f => f.ArrId == 601).MissingSince.Should().BeNull();
        }

        [Test]
        public void a_title_that_cannot_be_read_should_change_nothing()
        {
            GivenTwoSeriesImported();
            _application.Setup(a => a.FetchTitle(10)).Throws(new InvalidOperationException("connection refused"));

            Subject.RefreshTitle(ApplicationId, 10);

            EpisodeFiles.Should().OnlyContain(f => f.MissingSince == null);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void reading_one_movie_again_should_touch_only_that_movie()
        {
            _application.SetupGet(a => a.Definition).Returns(new ApplicationDefinition { Id = ApplicationId, Name = "Radarr", Implementation = "Radarr" });
            _catalog = new ApplicationCatalog();
            _catalog.Movies.Add(new CatalogMovie { ArrId = 20, TmdbId = 10331, Title = "Night of the Living Dead", ArrMovieFileId = 701 });
            _catalog.Movies.Add(new CatalogMovie { ArrId = 21, TmdbId = 10513, Title = "Plan 9 from Outer Space", ArrMovieFileId = 702 });
            _catalog.MovieFiles.Add(new CatalogMovieFile { ArrId = 701, ArrMovieId = 20, Path = "/movies/night.mkv", Size = 1 });
            _catalog.MovieFiles.Add(new CatalogMovieFile { ArrId = 702, ArrMovieId = 21, Path = "/movies/plan9.mkv", Size = 1 });
            Subject.Refresh(null, false);

            // Radarr upgraded Night of the Living Dead: a new file in place of the old one.
            var night = new ApplicationCatalog();
            night.Movies.Add(new CatalogMovie { ArrId = 20, TmdbId = 10331, Title = "Night of the Living Dead", ArrMovieFileId = 703 });
            night.MovieFiles.Add(new CatalogMovieFile { ArrId = 703, ArrMovieId = 20, Path = "/movies/night-1080p.mkv", Size = 2 });
            _application.Setup(a => a.FetchTitle(20)).Returns(night);

            Subject.RefreshTitle(ApplicationId, 20);

            var files = Mocker.Resolve<ICatalogMovieFileRepository>().ForApplication(ApplicationId);

            files.Single(f => f.ArrId == 703).MissingSince.Should().BeNull();
            files.Single(f => f.ArrId == 701).MissingSince.Should().NotBeNull();
            files.Single(f => f.ArrId == 702).MissingSince.Should().BeNull();
        }

        [Test]
        public void reading_one_artist_again_should_touch_only_that_artist()
        {
            _application.SetupGet(a => a.Definition).Returns(new ApplicationDefinition { Id = ApplicationId, Name = "Lidarr", Implementation = "Lidarr" });
            _catalog = new ApplicationCatalog();
            _catalog.Artists.Add(new CatalogArtist { ArrId = 7, ForeignArtistId = "low-mbid", Name = "Low" });
            _catalog.Artists.Add(new CatalogArtist { ArrId = 8, ForeignArtistId = "slint-mbid", Name = "Slint" });
            _catalog.Albums.Add(new CatalogAlbum { ArrId = 70, ArrArtistId = 7, ForeignAlbumId = "things-we-lost", Title = "Things We Lost in the Fire", Year = 2001 });
            _catalog.Albums.Add(new CatalogAlbum { ArrId = 80, ArrArtistId = 8, ForeignAlbumId = "spiderland", Title = "Spiderland", Year = 1991 });
            _catalog.TrackFiles.Add(new CatalogTrackFile { ArrId = 701, ArrArtistId = 7, ArrAlbumId = 70, Path = "/music/Low/01.mp3", Size = 1, Quality = "MP3-320" });
            _catalog.TrackFiles.Add(new CatalogTrackFile { ArrId = 801, ArrArtistId = 8, ArrAlbumId = 80, Path = "/music/Slint/01.flac", Size = 1, Quality = "FLAC" });
            Subject.Refresh(null, false);

            // Lidarr upgraded the album to FLAC: a new track file in place of the old one.
            var low = new ApplicationCatalog();
            low.Artists.Add(new CatalogArtist { ArrId = 7, ForeignArtistId = "low-mbid", Name = "Low" });
            low.Albums.Add(new CatalogAlbum { ArrId = 70, ArrArtistId = 7, ForeignAlbumId = "things-we-lost", Title = "Things We Lost in the Fire", Year = 2001 });
            low.TrackFiles.Add(new CatalogTrackFile { ArrId = 702, ArrArtistId = 7, ArrAlbumId = 70, Path = "/music/Low/01.flac", Size = 2, Quality = "FLAC" });
            _application.Setup(a => a.FetchTitle(7)).Returns(low);

            Subject.RefreshTitle(ApplicationId, 7);

            var files = Mocker.Resolve<ICatalogTrackFileRepository>().ForApplication(ApplicationId);

            files.Single(f => f.ArrId == 702).MissingSince.Should().BeNull();
            files.Single(f => f.ArrId == 701).MissingSince.Should().NotBeNull();
            files.Single(f => f.ArrId == 801).MissingSince.Should().BeNull();
            Mocker.Resolve<ICatalogAlbumRepository>().ForApplication(ApplicationId).Should().OnlyContain(a => a.MissingSince == null);
            Mocker.Resolve<ICatalogArtistRepository>().ForApplication(ApplicationId).Should().HaveCount(2);
        }

        // The Posters view of the local catalog: one entry for each series or movie with a file, by sort
        // title, with how many files the Arr app still reports.
        [Test]
        public void titles_should_count_the_files_each_series_and_movie_has()
        {
            Mocker.Resolve<ICatalogSeriesRepository>().InsertMany(new List<CatalogSeries>
            {
                new() { ApplicationId = 1, ArrId = 10, Title = "The Wire", SortTitle = "wire", Year = 2002 },
                new() { ApplicationId = 1, ArrId = 11, Title = "No Files Yet", SortTitle = "no files yet", Year = 2020 },
                new() { ApplicationId = 1, ArrId = 12, Title = "Gone", SortTitle = "gone", Year = 1999, MissingSince = DateTime.UtcNow }
            });
            Mocker.Resolve<ICatalogEpisodeFileRepository>().InsertMany(new List<CatalogEpisodeFile>
            {
                new() { ApplicationId = 1, ArrId = 100, ArrSeriesId = 10, Path = "/tv/w/1.mkv" },
                new() { ApplicationId = 1, ArrId = 101, ArrSeriesId = 10, Path = "/tv/w/2.mkv" },
                new() { ApplicationId = 1, ArrId = 102, ArrSeriesId = 10, Path = "/tv/w/3.mkv", MissingSince = DateTime.UtcNow },
                new() { ApplicationId = 1, ArrId = 103, ArrSeriesId = 12, Path = "/tv/g/1.mkv" }
            });
            Mocker.Resolve<ICatalogMovieRepository>().InsertMany(new List<CatalogMovie>
            {
                new() { ApplicationId = 2, ArrId = 20, Title = "Akira", SortTitle = "akira", Year = 1988 }
            });
            Mocker.Resolve<ICatalogMovieFileRepository>().InsertMany(new List<CatalogMovieFile>
            {
                new() { ApplicationId = 2, ArrId = 200, ArrMovieId = 20, Path = "/movies/a.mkv" }
            });

            var titles = Subject.GetTitles(null);

            titles.Select(t => (t.Title, t.Subject, t.ArrId, t.Files)).Should().Equal(
                ("Akira", PosterSubject.Movie, 20, 1),
                ("The Wire", PosterSubject.Series, 10, 2));

            Subject.GetTitles(2).Select(t => t.Title).Should().Equal("Akira");
        }
    }
}

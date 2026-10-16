using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Test.PeerCatalog
{
    [TestFixture]
    public class LocalHoldingsFixture
    {
        private List<CatalogSeries> _series;
        private List<CatalogEpisode> _episodes;
        private List<CatalogEpisodeFile> _episodeFiles;
        private List<CatalogMovie> _movies;
        private List<CatalogMovieFile> _movieFiles;

        [SetUp]
        public void Setup()
        {
            // Locally: Pingu with a 720p web file for episode 1 and nothing for episodes 2, 4 and 5,
            // and one movie with a 720p web file.
            _series = new List<CatalogSeries> { new() { ApplicationId = 1, ArrId = 10, TvdbId = 75669, ImdbId = "tt0366069" } };
            _episodes = new List<CatalogEpisode>
            {
                new() { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 501 },
                new() { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 2 },
                new() { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 4 },
                new() { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 5 }
            };
            _episodeFiles = new List<CatalogEpisodeFile> { new() { ApplicationId = 1, ArrId = 501, Resolution = 720, QualitySource = "web" } };
            _movies = new List<CatalogMovie> { new() { ApplicationId = 2, ArrId = 20, TmdbId = 10331, ImdbId = "tt0063350", ArrMovieFileId = 701 } };
            _movieFiles = new List<CatalogMovieFile> { new() { ApplicationId = 2, ArrId = 701, Resolution = 720, QualitySource = "webdl" } };
        }

        private PeerItemStatus Classify(PeerCatalogItem item)
        {
            return new LocalHoldings(_series, _episodes, _episodeFiles, _movies, _movieFiles).Classify(item);
        }

        private static PeerCatalogItem Episode(string episodes, int resolution = 720, string source = "web", int tvdbId = 75669)
        {
            return new PeerCatalogItem { MediaKind = MediaKind.Tv, TvdbId = tvdbId, SeasonNumber = 1, EpisodeNumbers = episodes, Resolution = resolution, QualitySource = source };
        }

        private static PeerCatalogItem Movie(int tmdbId, int resolution = 720, string source = "webdl", string imdbId = null)
        {
            return new PeerCatalogItem { MediaKind = MediaKind.Movie, TmdbId = tmdbId, ImdbId = imdbId, Resolution = resolution, QualitySource = source };
        }

        [Test]
        public void a_series_no_local_arr_app_has_should_be_not_in_library()
        {
            Classify(Episode("1", tvdbId: 999)).Should().Be(PeerItemStatus.NotInLibrary);
        }

        [Test]
        public void an_episode_without_a_local_file_should_be_missing()
        {
            Classify(Episode("2")).Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void an_episode_sonarr_does_not_list_should_be_missing()
        {
            Classify(Episode("99")).Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void the_same_quality_should_be_had()
        {
            Classify(Episode("1")).Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void a_lower_resolution_should_be_had()
        {
            Classify(Episode("1", 480, "bluray")).Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void a_higher_resolution_should_be_an_upgrade()
        {
            Classify(Episode("1", 1080)).Should().Be(PeerItemStatus.Upgrade);
        }

        [Test]
        public void a_better_source_at_the_same_resolution_should_be_an_upgrade()
        {
            Classify(Episode("1", 720, "bluray")).Should().Be(PeerItemStatus.Upgrade);
            Classify(Episode("1", 720, "television")).Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void a_multi_episode_file_should_be_missing_if_any_of_its_episodes_is()
        {
            Classify(Episode("1,2")).Should().Be(PeerItemStatus.Missing);
            Classify(Episode("4,5")).Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void a_local_file_the_arr_app_no_longer_reports_should_not_count()
        {
            _episodeFiles[0].MissingSince = DateTime.UtcNow;

            Classify(Episode("1")).Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void a_series_should_be_matched_by_imdb_id_when_there_is_no_tvdb_id()
        {
            var item = Episode("1", tvdbId: 0);

            item.ImdbId = "TT0366069";

            Classify(item).Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void movies_should_be_compared_the_same_way()
        {
            Classify(Movie(555)).Should().Be(PeerItemStatus.NotInLibrary);
            Classify(Movie(10331)).Should().Be(PeerItemStatus.Have);
            Classify(Movie(10331, 2160)).Should().Be(PeerItemStatus.Upgrade);
            Classify(Movie(0, imdbId: "tt0063350")).Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void a_movie_without_a_local_file_should_be_missing()
        {
            _movieFiles.Clear();

            Classify(Movie(10331)).Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void a_tv_id_should_never_match_a_movie()
        {
            // The same number can be a TVDB series and a TMDb movie.
            _movies[0].TmdbId = 75669;

            var item = Episode("1", tvdbId: 0);

            item.TmdbId = 75669;

            Classify(item).Should().Be(PeerItemStatus.NotInLibrary);
        }

        // Whisparr v2 and v3 know a scene by different IDs. They agree on its studio and its day.
        [TestCase("ATK Girlfriends", "2022-11-28", PeerItemStatus.Missing)]
        [TestCase("ATKGirlfriends", "2022-11-28", PeerItemStatus.Missing)]
        [TestCase("ATK Girlfriends", "2022-11-29", PeerItemStatus.NotInLibrary)]
        public void a_scene_from_the_other_whisparr_should_be_found_by_its_studio_and_day(string studio, string day, PeerItemStatus expected)
        {
            _movies.Add(new CatalogMovie { ApplicationId = 3, ArrId = 50, ItemType = "scene", Studio = "ATK Girlfriends", ReleaseDate = "2022-11-28", ForeignId = "whisparr2:9001" });

            var scene = new PeerCatalogItem
            {
                MediaKind = MediaKind.Movie, IsAdult = true, ItemType = "scene", Studio = studio, ReleaseDate = day,
                ForeignId = "0025f120-38cf-4e8c-a002-240b9416a439", StashId = "0025f120-38cf-4e8c-a002-240b9416a439", Resolution = 1080
            };

            Classify(scene).Should().Be(expected);
        }
    }
}

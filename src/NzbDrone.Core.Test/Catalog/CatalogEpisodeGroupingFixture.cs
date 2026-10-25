using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Catalog
{
    [TestFixture]
    public class CatalogEpisodeGroupingFixture : TestBase
    {
        [Test]
        public void a_multi_episode_file_is_one_row_with_both_episode_numbers_and_titles()
        {
            var episodes = new[]
            {
                new CatalogEpisode { Id = 1, ArrId = 101, SeasonNumber = 1, EpisodeNumber = 4, Title = "Part One", ArrEpisodeFileId = 50 },
                new CatalogEpisode { Id = 2, ArrId = 102, SeasonNumber = 1, EpisodeNumber = 5, Title = "Part Two", ArrEpisodeFileId = 50 }
            };
            var files = new[] { new CatalogEpisodeFile { ArrId = 50, SeasonNumber = 1, Path = "S01E04E05.mkv", Size = 2048 } };

            var season = CatalogEpisodeGrouping.Build("standard", episodes, files).Single();

            season.Files.Should().ContainSingle();
            season.Files[0].Episodes.Select(e => e.EpisodeNumber).Should().Equal(4, 5);
            season.Files[0].Episodes.Select(e => e.Title).Should().Equal("Part One", "Part Two");
            season.EpisodeCount.Should().Be(2);
            season.Size.Should().Be(2048);
        }

        [Test]
        public void anime_rows_show_and_sort_by_absolute_episode_numbers()
        {
            var episodes = new[]
            {
                new CatalogEpisode { Id = 1, SeasonNumber = 1, EpisodeNumber = 2, AbsoluteEpisodeNumber = 12 },
                new CatalogEpisode { Id = 2, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 11 }
            };

            var season = CatalogEpisodeGrouping.Build("anime", episodes, Array.Empty<CatalogEpisodeFile>()).Single();

            season.Files.Select(f => CatalogEpisodeGrouping.EpisodeLabel(f.Episodes[0], "anime")).Should().Equal("#011", "#012");
        }

        [Test]
        public void scene_numbers_are_secondary_to_the_tvdb_season_and_episode()
        {
            var episode = new CatalogEpisode
            {
                Id = 1,
                SeasonNumber = 2,
                EpisodeNumber = 3,
                SceneSeasonNumber = 1,
                SceneEpisodeNumber = 4
            };

            var season = CatalogEpisodeGrouping.Build("standard", new[] { episode }, Array.Empty<CatalogEpisodeFile>()).Single();

            season.SeasonNumber.Should().Be(2);
            CatalogEpisodeGrouping.EpisodeLabel(episode, "standard").Should().Be("S02E03");
            episode.SceneSeasonNumber.Should().Be(1);
            episode.SceneEpisodeNumber.Should().Be(4);
        }

        [Test]
        public void daily_rows_are_ordered_by_air_date_within_sonarr_season()
        {
            var episodes = new[]
            {
                new CatalogEpisode { Id = 1, SeasonNumber = 2, EpisodeNumber = 2, AirDateUtc = new DateTime(2026, 4, 2) },
                new CatalogEpisode { Id = 2, SeasonNumber = 2, EpisodeNumber = 1, AirDateUtc = new DateTime(2026, 4, 1) }
            };

            var season = CatalogEpisodeGrouping.Build("daily", episodes, Array.Empty<CatalogEpisodeFile>()).Single();

            season.Files.Select(CatalogEpisodeGrouping.AirDate).Should().Equal(new DateTime(2026, 4, 1), new DateTime(2026, 4, 2));
        }

        [Test]
        public void special_season_zero_is_last()
        {
            var episodes = new[]
            {
                new CatalogEpisode { Id = 1, SeasonNumber = 0, EpisodeNumber = 1 },
                new CatalogEpisode { Id = 2, SeasonNumber = 1, EpisodeNumber = 1 },
                new CatalogEpisode { Id = 3, SeasonNumber = 2, EpisodeNumber = 1 }
            };

            CatalogEpisodeGrouping.Build("standard", episodes, Array.Empty<CatalogEpisodeFile>())
                .Select(s => s.SeasonNumber)
                .Should().Equal(1, 2, 0);
        }
    }
}

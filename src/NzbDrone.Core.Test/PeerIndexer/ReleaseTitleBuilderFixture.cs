using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PeerIndexer
{
    // The parser these tests read titles back with is Sonarr's own, which this code base carries.
    [TestFixture]
    public class ReleaseTitleBuilderFixture : CoreTest
    {
        private static PeerCatalogItem Episode(string quality = "WEBDL-720p", string episodes = "1")
        {
            return new PeerCatalogItem { MediaKind = MediaKind.Tv, Title = "Pingu", Year = 1989, SeasonNumber = 1, EpisodeNumbers = episodes, Quality = quality };
        }

        private static PeerCatalogItem Movie()
        {
            return new PeerCatalogItem { MediaKind = MediaKind.Movie, Title = "Night of the Living Dead", Year = 1968, Quality = "Bluray-1080p" };
        }

        public static IEnumerable<TestCaseData> SonarrQualities => Quality.All.Where(q => q != Quality.Unknown).Select(q => new TestCaseData(q).SetName("sonarr_should_read_back_" + q.Name.Replace('-', '_').Replace(' ', '_')));

        [TestCaseSource(nameof(SonarrQualities))]
        public void a_built_title_should_be_read_back_as_the_same_quality(Quality quality)
        {
            var title = ReleaseTitleBuilder.Build(Episode(quality.Name));

            QualityParser.ParseQuality(title).Quality.Should().Be(quality, title);
        }

        // Radarr names a few qualities differently. Its parser is not in this code base, so these
        // only pin the release-name words chosen for them.
        [TestCase("Remux-1080p", "1080p.BluRay.Remux")]
        [TestCase("Remux-2160p", "2160p.BluRay.Remux")]
        [TestCase("Bluray-576p", "576p.BluRay")]
        [TestCase("WEBRip-1080p", "1080p.WEBRip")]
        [TestCase("BR-DISK", "BR-DISK")]
        [TestCase("DVD-R", "DVD-R")]
        [TestCase("TELESYNC", "TELESYNC")]
        [TestCase("Unknown", "")]
        [TestCase(null, "")]
        public void a_quality_name_should_become_release_name_words(string quality, string expected)
        {
            ReleaseTitleBuilder.QualityTokens(quality).Should().Be(expected);
        }

        [Test]
        public void a_built_episode_title_should_be_read_back_as_the_same_episode()
        {
            var item = Episode();

            item.AudioCodec = "EAC3 Atmos";
            item.AudioChannels = 5.1;
            item.VideoCodec = "h265";
            item.VideoDynamicRange = "DV HDR10";
            item.ReleaseGroup = "NODEARR";

            var title = ReleaseTitleBuilder.For(item);

            title.Should().Be("Pingu.S01E01.720p.WEB-DL.DV.HDR10.EAC3.Atmos.5.1.h265-NODEARR");

            var parsed = Parser.Parser.ParseTitle(title);

            parsed.SeriesTitle.Should().Be("Pingu");
            parsed.SeasonNumber.Should().Be(1);
            parsed.EpisodeNumbers.Should().Equal(1);
            parsed.Quality.Quality.Should().Be(Quality.WEBDL720p);
            parsed.ReleaseGroup.Should().Be("NODEARR");
        }

        [Test]
        public void a_file_with_several_episodes_should_name_them_all()
        {
            var title = ReleaseTitleBuilder.For(Episode(episodes: "4,5"));

            title.Should().Be("Pingu.S01E04E05.720p.WEB-DL");
            Parser.Parser.ParseTitle(title).EpisodeNumbers.Should().Equal(4, 5);
        }

        [TestCase("Marvel's Agents of S.H.I.E.L.D.", "Marvels.Agents.of.S.H.I.E.L.D")]
        [TestCase("Law & Order", "Law.and.Order")]
        [TestCase("Doctor Who (2005)", "Doctor.Who.2005")]
        [TestCase("Spider-Man: No Way Home", "Spider.Man.No.Way.Home")]
        [TestCase("", "Unknown")]
        public void a_title_should_be_written_as_release_name_words(string title, string expected)
        {
            var item = Episode();

            item.Title = title;

            ReleaseTitleBuilder.For(item).Should().StartWith(expected + ".S01E01.");
        }

        [Test]
        public void a_built_movie_title_should_carry_the_year_and_the_edition()
        {
            var item = Movie();

            item.Edition = "Director's Cut";
            item.AudioCodec = "DTS-HD MA";
            item.AudioChannels = 2;

            ReleaseTitleBuilder.For(item).Should().Be("Night.of.the.Living.Dead.1968.Directors.Cut.1080p.BluRay.DTS-HD.MA.2.0");
        }

        [Test]
        public void a_language_other_than_english_should_be_named()
        {
            var item = Episode();

            item.Languages = "German, English";

            var title = ReleaseTitleBuilder.For(item);

            title.Should().Be("Pingu.S01E01.German.720p.WEB-DL");
            Parser.Parser.ParseTitle(title).Languages.Select(l => l.Name).Should().Contain("German");
        }

        [Test]
        public void the_name_the_release_was_grabbed_under_should_be_used_when_there_is_one()
        {
            var item = Episode();

            item.SceneName = "Pingu.S01E01.720p.WEB-DL.AAC2.0.H.264-NODEARR";

            ReleaseTitleBuilder.For(item).Should().Be("Pingu.S01E01.720p.WEB-DL.AAC2.0.H.264-NODEARR");
        }

        [TestCase("holiday video")]
        [TestCase("../../Pingu.S01E01.720p.WEB-DL")]
        [TestCase("Pingu.S01E01\n720p")]
        [TestCase("   ")]
        public void a_scene_name_an_arr_app_could_not_use_should_be_replaced(string sceneName)
        {
            var item = Episode();

            item.SceneName = sceneName;

            ReleaseTitleBuilder.For(item).Should().Be("Pingu.S01E01.720p.WEB-DL");
        }

        [Test]
        public void a_movie_scene_name_should_need_the_year()
        {
            var item = Movie();

            item.SceneName = "Night.of.the.Living.Dead.1968.1080p.BluRay.x264-GROUP";
            ReleaseTitleBuilder.For(item).Should().Be("Night.of.the.Living.Dead.1968.1080p.BluRay.x264-GROUP");

            item.SceneName = "notld-1080p";
            ReleaseTitleBuilder.For(item).Should().Be("Night.of.the.Living.Dead.1968.1080p.BluRay");
        }

        private static PeerCatalogItem DailyEpisode(string sceneName = null)
        {
            return new PeerCatalogItem
            {
                MediaKind = MediaKind.Tv, Title = "The Daily Show", SeasonNumber = 29, EpisodeNumbers = "45", AirDates = "2024-05-17",
                Quality = "WEBDL-720p", SceneName = sceneName
            };
        }

        [Test]
        public void a_title_asked_for_by_day_should_be_written_with_the_day_and_read_back_as_that_day()
        {
            var title = ReleaseTitleBuilder.For(DailyEpisode(), "2024-05-17");

            title.Should().Be("The.Daily.Show.2024.05.17.720p.WEB-DL");

            var parsed = Parser.Parser.ParseTitle(title);

            parsed.IsDaily.Should().BeTrue();
            parsed.AirDate.Should().Be("2024-05-17");
            parsed.SeriesTitle.Should().Be("The Daily Show");
        }

        [Test]
        public void the_same_file_asked_for_by_number_should_be_written_with_its_number()
        {
            var parsed = Parser.Parser.ParseTitle(ReleaseTitleBuilder.For(DailyEpisode()));

            parsed.IsDaily.Should().BeFalse();
            parsed.SeasonNumber.Should().Be(29);
            parsed.EpisodeNumbers.Should().Equal(45);
        }

        [Test]
        public void a_scene_name_written_by_day_should_be_kept_only_for_a_search_for_that_day()
        {
            const string sceneName = "The.Daily.Show.2024.05.17.Some.Guest.1080p.WEB.h264-GROUP";

            var item = DailyEpisode(sceneName);

            ReleaseTitleBuilder.For(item, "2024-05-17").Should().Be(sceneName);

            // Sonarr turns down a title with a day in it when it asked by number.
            ReleaseTitleBuilder.For(item).Should().Be("The.Daily.Show.S29E45.720p.WEB-DL");

            // And one with the wrong day when it asked by day.
            ReleaseTitleBuilder.For(item, "2024-05-18").Should().Be("The.Daily.Show.2024.05.18.720p.WEB-DL");
        }

        [Test]
        public void a_scene_name_written_by_number_should_not_be_used_for_a_search_by_day()
        {
            var item = DailyEpisode("The.Daily.Show.S29E45.1080p.WEB.h264-GROUP");

            ReleaseTitleBuilder.For(item).Should().Be("The.Daily.Show.S29E45.1080p.WEB.h264-GROUP");
            ReleaseTitleBuilder.For(item, "2024-05-17").Should().Be("The.Daily.Show.2024.05.17.720p.WEB-DL");
        }

        // Sonarr reads the numbers in an indexer's title as the scene's for a series that has them.
        [Test]
        public void a_built_title_should_number_episodes_the_way_the_scene_does_when_it_differs()
        {
            var item = Episode(episodes: "62,63");

            item.SceneSeasonNumber = 2;
            item.SceneEpisodeNumbers = "1,2";

            var title = ReleaseTitleBuilder.Build(item);
            var parsed = Parser.Parser.ParseTitle(title);

            title.Should().StartWith("Pingu.S02E01E02.");
            parsed.SeasonNumber.Should().Be(2);
            parsed.EpisodeNumbers.Should().Equal(1, 2);
        }

        [Test]
        public void a_title_by_day_should_not_be_changed_by_scene_numbers()
        {
            var item = Episode();

            item.SceneSeasonNumber = 2;
            item.SceneEpisodeNumbers = "7";

            ReleaseTitleBuilder.For(item, "2024-05-17").Should().StartWith("Pingu.2024.05.17.");
        }
    }
}

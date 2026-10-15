using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Test.PeerCatalog
{
    // "Better than yours" as the local Arr app's quality profile decides it.
    [TestFixture]
    public class ProfileUpgradeFixture
    {
        private const int ProfileId = 3;

        private ArrQualityProfileResource _profile;
        private CatalogEpisodeFile _file;

        [SetUp]
        public void Setup()
        {
            // As Sonarr lists a profile: the least wanted first. WEB 720p is a group, Bluray-1080p
            // is not allowed, and the cutoff is WEBDL-1080p.
            _profile = new ArrQualityProfileResource
            {
                Id = ProfileId,
                UpgradeAllowed = true,
                Cutoff = 3,
                Items = new List<ArrQualityProfileItemResource>
                {
                    Quality(4, "HDTV-720p", true),
                    new()
                    {
                        Id = 1001,
                        Name = "WEB 720p",
                        Allowed = true,
                        Items = new List<ArrQualityProfileItemResource> { Quality(5, "WEBDL-720p", true), Quality(14, "WEBRip-720p", true) }
                    },
                    Quality(6, "Bluray-720p", true),
                    Quality(3, "WEBDL-1080p", true),
                    Quality(7, "Bluray-1080p", false),
                    Quality(19, "Bluray-2160p", true)
                }
            };

            _file = new CatalogEpisodeFile { ApplicationId = 1, ArrId = 501, Quality = "WEBDL-720p", Resolution = 720, QualitySource = "web" };
        }

        private static ArrQualityProfileItemResource Quality(int id, string name, bool allowed)
        {
            return new ArrQualityProfileItemResource { Allowed = allowed, Quality = new ArrQualityResource { Id = id, Name = name } };
        }

        private PeerItemStatus Classify(string quality, int resolution, string source)
        {
            var profile = _profile.ToCatalog();
            profile.ApplicationId = 1;

            var holdings = new LocalHoldings(
                new[] { new CatalogSeries { ApplicationId = 1, ArrId = 10, TvdbId = 75669, QualityProfileId = ProfileId } },
                new[] { new CatalogEpisode { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 501 } },
                new[] { _file },
                new CatalogMovie[0],
                new CatalogMovieFile[0],
                new[] { profile });

            return holdings.Classify(new PeerCatalogItem
            {
                MediaKind = MediaKind.Tv,
                TvdbId = 75669,
                SeasonNumber = 1,
                EpisodeNumbers = "1",
                Quality = quality,
                Resolution = resolution,
                QualitySource = source
            });
        }

        [Test]
        public void a_quality_the_profile_ranks_higher_should_be_an_upgrade()
        {
            Classify("Bluray-720p", 720, "bluray").Should().Be(PeerItemStatus.Upgrade);
        }

        [Test]
        public void a_quality_the_profile_does_not_allow_should_not_be_an_upgrade_whatever_its_resolution()
        {
            Classify("Bluray-1080p", 1080, "bluray").Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void a_quality_the_profile_does_not_list_should_not_be_an_upgrade()
        {
            Classify("HDTV-1080p", 1080, "television").Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void a_quality_in_the_same_group_should_not_be_an_upgrade()
        {
            Classify("WEBRip-720p", 720, "webRip").Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void nothing_should_be_an_upgrade_once_the_cutoff_is_met()
        {
            _file.Quality = "WEBDL-1080p";
            _file.Resolution = 1080;

            Classify("Bluray-2160p", 2160, "bluray").Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void nothing_should_be_an_upgrade_when_the_profile_does_not_upgrade()
        {
            _profile.UpgradeAllowed = false;

            Classify("Bluray-720p", 720, "bluray").Should().Be(PeerItemStatus.Have);
        }

        [Test]
        public void a_lower_resolution_the_profile_prefers_should_be_an_upgrade()
        {
            // This profile wants HDTV-720p least, and the local file is that.
            _file.Quality = "HDTV-720p";

            Classify("WEBDL-720p", 720, "web").Should().Be(PeerItemStatus.Upgrade);
        }

        [Test]
        public void without_a_ranking_the_simple_rule_should_stand_in()
        {
            var holdings = new LocalHoldings(
                new[] { new CatalogSeries { ApplicationId = 1, ArrId = 10, TvdbId = 75669, QualityProfileId = ProfileId } },
                new[] { new CatalogEpisode { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 501 } },
                new[] { _file },
                new CatalogMovie[0],
                new CatalogMovieFile[0],
                new[] { new CatalogQualityProfile { ApplicationId = 1, ArrId = ProfileId } });

            holdings.Classify(new PeerCatalogItem { MediaKind = MediaKind.Tv, TvdbId = 75669, SeasonNumber = 1, EpisodeNumbers = "1", Quality = "Bluray-1080p", Resolution = 1080, QualitySource = "bluray" })
                .Should().Be(PeerItemStatus.Upgrade);
        }

        [Test]
        public void a_profile_should_be_ranked_from_its_items_with_a_group_sharing_one_rank()
        {
            var ranking = _profile.ToRanking();

            ranking.Find("HDTV-720p").Rank.Should().Be(0);
            ranking.Find("WEBDL-720p").Rank.Should().Be(1);
            ranking.Find("webrip-720p").Rank.Should().Be(1);
            ranking.Find("Bluray-1080p").Allowed.Should().BeFalse();
            ranking.CutoffRank.Should().Be(3);
        }

        [Test]
        public void a_cutoff_that_names_a_group_should_be_that_groups_rank()
        {
            _profile.Cutoff = 1001;

            _profile.ToRanking().CutoffRank.Should().Be(1);
        }

        [Test]
        public void a_ranking_should_survive_being_stored()
        {
            var stored = QualityRanking.FromStored(_profile.ToRanking().ToStored());

            stored.CutoffRank.Should().Be(3);
            stored.Find("Bluray-720p").Rank.Should().Be(2);
            stored.UpgradeAllowed.Should().BeTrue();
        }
    }
}

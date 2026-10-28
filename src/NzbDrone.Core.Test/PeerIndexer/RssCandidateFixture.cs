using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PeerIndexer
{
    [TestFixture]
    public class RssCandidateFixture : DbTest<CandidateService, PeerCatalogItem>
    {
        private readonly DateTime _baseline = DateTime.UtcNow.AddDays(-2);
        private Peer _peer;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());
            Mocker.SetConstant<ILocalHoldingsFactory>(Mocker.Resolve<LocalHoldingsFactory>());
            _peer = Mocker.Resolve<IPeerRepository>().Insert(new Peer
            {
                NodeId = "alice", Name = "Alice", State = PeerState.Trusted,
                StateChanged = _baseline.AddDays(-1), CatalogFirstSyncedAt = _baseline
            });

            // Two Sonarrs know the same series. Only app 1 already has episode 1.
            Mocker.GetMock<ICatalogSeriesRepository>().Setup(r => r.All()).Returns(new List<CatalogSeries>
            {
                new() { ApplicationId = 1, ArrId = 10, TvdbId = 75669 },
                new() { ApplicationId = 2, ArrId = 20, TvdbId = 75669 }
            });
            Mocker.GetMock<ICatalogEpisodeRepository>().Setup(r => r.All()).Returns(new List<CatalogEpisode>
            {
                new() { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 30 }
            });
            Mocker.GetMock<ICatalogEpisodeFileRepository>().Setup(r => r.All()).Returns(new List<CatalogEpisodeFile>
            {
                new() { ApplicationId = 1, ArrId = 30, Resolution = 720, QualitySource = "web" }
            });
            Mocker.GetMock<ICatalogMovieRepository>().Setup(r => r.All()).Returns(new List<CatalogMovie>());
            Mocker.GetMock<ICatalogMovieFileRepository>().Setup(r => r.All()).Returns(new List<CatalogMovieFile>());
            Mocker.GetMock<ICatalogQualityProfileRepository>().Setup(r => r.All()).Returns(new List<CatalogQualityProfile>());
            Mocker.GetMock<ICatalogArtistRepository>().Setup(r => r.All()).Returns(new List<CatalogArtist>());
            Mocker.GetMock<ICatalogAlbumRepository>().Setup(r => r.All()).Returns(new List<CatalogAlbum>());
            Mocker.GetMock<ICatalogTrackFileRepository>().Setup(r => r.All()).Returns(new List<CatalogTrackFile>());
        }

        private PeerCatalogItem Offer(int episode, int resolution = 720, DateTime? firstSeen = null)
        {
            return Db.Insert(new PeerCatalogItem
            {
                PeerId = _peer.Id, RemoteId = $"e-{episode}-{resolution}", CandidateId = $"e-{episode}-{resolution}",
                MediaKind = MediaKind.Tv, TvdbId = 75669, SeasonNumber = 1, EpisodeNumbers = episode.ToString(),
                Resolution = resolution, QualitySource = "web", FirstSeen = firstSeen ?? _baseline.AddDays(1),
                DateAdded = _baseline.AddYears(-1), LocalStatus = PeerItemStatus.Have
            });
        }

        private static CandidateQuery Browse(int app = 1, bool upgrades = true, int offset = 0, int limit = 100)
        {
            return new CandidateQuery { RssApplicationId = app, IncludeUpgrades = upgrades, Offset = offset, Limit = limit };
        }

        [Test]
        public void rss_should_include_missing_and_exclude_have_and_titles_outside_this_app()
        {
            Offer(1);
            var missing = Offer(2);
            var other = Offer(3);
            other.TvdbId = 999;
            Mocker.Resolve<IPeerCatalogRepository>().Update(other);

            Subject.Search(Browse()).Items.Should().ContainSingle(i => i.Id == missing.Id);
        }

        [Test]
        public void each_sonarr_should_get_its_own_missing_episodes()
        {
            Offer(1);
            Subject.Search(Browse(1)).Items.Should().BeEmpty();
            Subject.Search(Browse(2)).Items.Should().ContainSingle();
            Subject.Search(Browse(999)).Items.Should().BeEmpty();
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        public void upgrades_should_only_be_included_when_allowed(bool upgrades, int count)
        {
            Offer(1, 1080);
            Subject.Search(Browse(upgrades: upgrades)).Items.Should().HaveCount(count);
        }

        [Test]
        public void rss_should_use_the_requesting_apps_quality_profile_even_when_profile_ids_collide()
        {
            Mocker.GetMock<ICatalogSeriesRepository>().Setup(r => r.All()).Returns(new List<CatalogSeries>
            {
                new() { ApplicationId = 1, ArrId = 10, TvdbId = 75669, QualityProfileId = 1 },
                new() { ApplicationId = 2, ArrId = 20, TvdbId = 75669, QualityProfileId = 1 }
            });
            Mocker.GetMock<ICatalogEpisodeRepository>().Setup(r => r.All()).Returns(new List<CatalogEpisode>
            {
                new() { ApplicationId = 1, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 30 },
                new() { ApplicationId = 2, ArrSeriesId = 20, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 30 }
            });
            Mocker.GetMock<ICatalogEpisodeFileRepository>().Setup(r => r.All()).Returns(new List<CatalogEpisodeFile>
            {
                new() { ApplicationId = 1, ArrId = 30, Quality = "WEBDL-720p", Resolution = 720, QualitySource = "web" },
                new() { ApplicationId = 2, ArrId = 30, Quality = "WEBDL-720p", Resolution = 720, QualitySource = "web" }
            });
            var profiles = new List<CatalogQualityProfile>();
            foreach (var app in new[] { 1, 2 })
            {
                var profile = new ArrQualityProfileResource
                {
                    Id = 1, UpgradeAllowed = true, Cutoff = 3,
                    Items = new List<ArrQualityProfileItemResource>
                    {
                        new() { Allowed = true, Quality = new ArrQualityResource { Id = 5, Name = "WEBDL-720p" } },
                        new() { Allowed = app == 2, Quality = new ArrQualityResource { Id = 3, Name = "WEBDL-1080p" } }
                    }
                }.ToCatalog();
                profile.ApplicationId = app;
                profiles.Add(profile);
            }

            Mocker.GetMock<ICatalogQualityProfileRepository>().Setup(r => r.All()).Returns(profiles);
            var offered = Offer(1, 1080);
            offered.Quality = "WEBDL-1080p";
            Mocker.Resolve<IPeerCatalogRepository>().Update(offered);
            Subject.Search(Browse(1)).Items.Should().BeEmpty();
            Subject.Search(Browse(2)).Items.Should().ContainSingle();
        }

        [Test]
        public void radarr_feed_should_use_its_own_movie_holdings()
        {
            Mocker.GetMock<ICatalogMovieRepository>().Setup(r => r.All()).Returns(new List<CatalogMovie>
            {
                new() { ApplicationId = 3, ArrId = 10, TmdbId = 10331 }
            });
            var offered = Offer(1);
            offered.MediaKind = MediaKind.Movie;
            offered.TmdbId = 10331;
            Mocker.Resolve<IPeerCatalogRepository>().Update(offered);
            Subject.Search(Browse(3)).Items.Should().ContainSingle();
            Subject.Search(Browse(1)).Items.Should().BeEmpty();
        }

        [TestCase(-1)]
        [TestCase(0)]
        public void the_initial_library_should_not_be_announced(int daysAfterBaseline)
        {
            Offer(2, firstSeen: _baseline.AddDays(daysAfterBaseline));
            Subject.Search(Browse()).Items.Should().BeEmpty();
        }

        [TestCase(ConnectionPath.Direct, true, 1)]
        [TestCase(ConnectionPath.Relay, true, 0)]
        [TestCase(ConnectionPath.Unknown, true, 0)]
        [TestCase(ConnectionPath.Unknown, false, 1)]
        public void rss_should_use_the_last_connection_check(ConnectionPath path, bool checkedConnection, int count)
        {
            Offer(2);
            _peer.ConnectionPath = path;
            _peer.ConnectionChecked = checkedConnection ? DateTime.UtcNow : null;
            Mocker.Resolve<IPeerRepository>().Update(_peer);
            Subject.Search(Browse()).Items.Should().HaveCount(count);
        }

        [Test]
        public void pairing_again_should_not_reannounce_previous_arrivals()
        {
            Offer(2);
            _peer.StateChanged = DateTime.UtcNow;
            Mocker.Resolve<IPeerRepository>().Update(_peer);
            Subject.Search(Browse()).Items.Should().BeEmpty();
        }

        [Test]
        public void filtering_should_happen_before_pagination_and_report_the_filtered_total()
        {
            Offer(1);
            Offer(2);
            Offer(3);
            var result = Subject.Search(Browse(offset: 1, limit: 1));
            result.Total.Should().Be(2);
            result.Items.Should().ContainSingle(i => i.EpisodeNumbers == "3");
        }

        [Test]
        public void legacy_browse_and_searches_should_still_find_the_initial_library()
        {
            Offer(1, firstSeen: _baseline);
            Subject.Search(new CandidateQuery()).Items.Should().ContainSingle();
            Subject.Search(new CandidateQuery { RssApplicationId = 1, TvdbId = 75669 }).Items.Should().ContainSingle();
        }
    }
}

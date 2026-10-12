using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PeerIndexer
{
    [TestFixture]
    public class CandidateServiceFixture : DbTest<CandidateService, PeerCatalogItem>
    {
        private const int PinguTvdbId = 75669;
        private const int NightTmdbId = 10331;
        private const int DailyTvdbId = 71256;

        private Peer _alice;
        private Peer _bob;
        private Peer _mallory;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());

            _alice = GivenPeer("Alice", PeerState.Trusted);
            _bob = GivenPeer("Bob", PeerState.Trusted);
            _mallory = GivenPeer("Mallory", PeerState.Revoked);

            GivenEpisode(_alice, "a-e1", 1, "1", 720, daysAgo: 30);
            GivenEpisode(_alice, "a-e2", 1, "2", 720, daysAgo: 20);
            GivenEpisode(_alice, "a-e45", 1, "4,5", 720, daysAgo: 10);
            GivenEpisode(_alice, "a-s2e1", 2, "1", 720, daysAgo: 5);
            GivenEpisode(_bob, "b-e1", 1, "1", 1080, daysAgo: 3);
            GivenEpisode(_mallory, "m-e1", 1, "1", 2160, daysAgo: 1);

            GivenMovie(_alice, "a-night", daysAgo: 2);

            // A different series that happens to share Night of the Living Dead's TMDb number,
            // because TMDb numbers TV and movies separately.
            Db.Insert(new PeerCatalogItem
            {
                PeerId = _alice.Id, RemoteId = "a-other", CandidateId = "a-other", MediaKind = MediaKind.Tv, TvdbId = 1, TmdbId = NightTmdbId,
                Title = "Bluey", SeasonNumber = 1, EpisodeNumbers = "1", ReleaseTitle = "Bluey.S01E01.1080p.WEB-DL", DateAdded = DateTime.UtcNow.AddDays(-40)
            });
        }

        private Peer GivenPeer(string name, PeerState state)
        {
            return Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = name.ToLowerInvariant(), Name = name, State = state, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });
        }

        private void GivenEpisode(Peer peer, string id, int season, string episodes, int resolution, int daysAgo)
        {
            Db.Insert(new PeerCatalogItem
            {
                PeerId = peer.Id, RemoteId = id, CandidateId = id, MediaKind = MediaKind.Tv, TvdbId = PinguTvdbId, ImdbId = "tt0366069",
                Title = "Pingu", Year = 1989, SeasonNumber = season, EpisodeNumbers = episodes, Resolution = resolution,
                ReleaseTitle = $"Pingu.S{season:00}{string.Concat(episodes.Split(',').Select(e => "E" + e.PadLeft(2, '0')))}.{resolution}p.WEB-DL", DateAdded = DateTime.UtcNow.AddDays(-daysAgo)
            });
        }

        private void GivenMovie(Peer peer, string id, int daysAgo)
        {
            Db.Insert(new PeerCatalogItem
            {
                PeerId = peer.Id, RemoteId = id, CandidateId = id, MediaKind = MediaKind.Movie, TmdbId = NightTmdbId, ImdbId = "tt0063350",
                Title = "Night of the Living Dead", Year = 1968, Resolution = 720,
                ReleaseTitle = "Night.of.the.Living.Dead.1968.720p.WEB-DL", DateAdded = DateTime.UtcNow.AddDays(-daysAgo)
            });
        }

        private List<string> Found(CandidateQuery query)
        {
            return Subject.Search(query).Items.Select(i => i.CandidateId).ToList();
        }

        private static CandidateQuery Tv(int? season = null, string episode = null)
        {
            return new CandidateQuery { IncludeMovies = false, TvdbId = PinguTvdbId, Season = season, Episode = episode };
        }

        [Test]
        public void an_episode_search_should_find_every_trusted_peers_copy_and_nobody_elses()
        {
            // Bob's is first because it is the better file. Mallory is not trusted.
            Found(Tv(1, "1")).Should().Equal("b-e1", "a-e1");
        }

        [Test]
        public void an_episode_search_should_find_a_file_that_holds_the_episode_among_others()
        {
            Found(Tv(1, "5")).Should().Equal("a-e45");
        }

        [Test]
        public void a_season_search_should_find_the_whole_season_in_order()
        {
            Found(Tv(1)).Should().Equal("b-e1", "a-e1", "a-e2", "a-e45");
        }

        [Test]
        public void a_series_search_should_find_every_season()
        {
            Found(Tv()).Should().HaveCount(5).And.Contain("a-s2e1");
        }

        [Test]
        public void a_file_should_match_when_any_id_it_carries_agrees()
        {
            // Sonarr sends the IMDb ID with "tt". This asks with the wrong TVDB ID and the right IMDb ID.
            Found(new CandidateQuery { IncludeMovies = false, TvdbId = 999999, ImdbId = "tt0366069", Season = 2 }).Should().Equal("a-s2e1");
        }

        [Test]
        public void a_movie_search_should_take_the_imdb_id_the_way_radarr_sends_it()
        {
            Found(new CandidateQuery { IncludeTv = false, ImdbId = "0063350" }).Should().Equal("a-night");
        }

        [Test]
        public void a_movie_search_should_not_find_a_series_with_the_same_tmdb_number()
        {
            Found(new CandidateQuery { IncludeTv = false, TmdbId = NightTmdbId }).Should().Equal("a-night");
            Found(new CandidateQuery { IncludeMovies = false, TmdbId = NightTmdbId }).Should().Equal("a-other");
        }

        [Test]
        public void a_search_for_something_nobody_offers_should_find_nothing()
        {
            Found(new CandidateQuery { TvdbId = 424242 }).Should().BeEmpty();
            Found(Tv(7)).Should().BeEmpty();
        }

        [Test]
        public void a_search_by_an_id_nodearr_does_not_hold_should_find_nothing_instead_of_everything()
        {
            var result = Subject.Search(new CandidateQuery { HasUnknownIds = true });

            result.Items.Should().BeEmpty();
            result.Total.Should().Be(0);
        }

        // A show Sonarr may ask for by day: season 29 runs across two years.
        private void GivenDailyShow()
        {
            GivenAired(_alice, "d-0517", 29, "45", "2024-05-17");
            GivenAired(_alice, "d-1231", 29, "120,121", "2024-12-30,2024-12-31");
            GivenAired(_alice, "d-0102", 29, "122", "2025-01-02");
            GivenAired(_mallory, "m-0517", 29, "45", "2024-05-17");
        }

        private void GivenAired(Peer peer, string id, int season, string episodes, string airDates)
        {
            Db.Insert(new PeerCatalogItem
            {
                PeerId = peer.Id, RemoteId = id, CandidateId = id, MediaKind = MediaKind.Tv, TvdbId = DailyTvdbId,
                Title = "The Daily Show", SeasonNumber = season, EpisodeNumbers = episodes, AirDates = airDates, Resolution = 720,
                ReleaseTitle = $"The.Daily.Show.S{season:00}E{episodes.Split(',')[0].PadLeft(2, '0')}.720p.WEB-DL", DateAdded = DateTime.UtcNow.AddDays(-1)
            });
        }

        private static CandidateQuery Daily(int? season = null, string episode = null)
        {
            return new CandidateQuery { IncludeMovies = false, TvdbId = DailyTvdbId, Season = season, Episode = episode };
        }

        [TestCase(2024, "05/17", "2024-05-17")]
        [TestCase(1990, "01/02", "1990-01-02")]
        [TestCase(2024, "13/40", null)]
        [TestCase(2024, "5", null)]
        [TestCase(1, "2", null)]
        [TestCase(null, "05/17", null)]
        [TestCase(2024, null, null)]
        public void a_query_should_be_by_day_only_when_it_is_a_year_and_a_month_and_day(int? season, string episode, string expected)
        {
            new CandidateQuery { Season = season, Episode = episode }.AirDate.Should().Be(expected);
        }

        [Test]
        public void a_search_by_day_should_find_the_episode_that_aired_that_day_and_say_so()
        {
            GivenDailyShow();

            var result = Subject.Search(Daily(2024, "05/17"));

            result.Items.Select(i => i.CandidateId).Should().Equal("d-0517");
            result.AirDates.Should().Equal(new Dictionary<string, string> { { "d-0517", "2024-05-17" } });
        }

        [Test]
        public void a_search_by_day_should_find_a_file_that_holds_that_day_among_others()
        {
            GivenDailyShow();

            var result = Subject.Search(Daily(2024, "12/31"));

            // The day asked for, not the first day in the file. Sonarr checks the title against it.
            result.AirDates.Should().Equal(new Dictionary<string, string> { { "d-1231", "2024-12-31" } });
        }

        [Test]
        public void a_search_by_day_should_find_nothing_when_nothing_aired_that_day()
        {
            GivenDailyShow();

            Found(Daily(2024, "05/18")).Should().BeEmpty();
        }

        [Test]
        public void a_search_by_day_should_not_find_a_file_whose_days_the_peer_did_not_give()
        {
            // Pingu's files carry no air dates, as from a node that does not send them yet.
            Found(Tv(2026, "10/10")).Should().BeEmpty();
        }

        [Test]
        public void a_search_for_a_year_should_find_what_aired_in_it()
        {
            GivenDailyShow();

            var result = Subject.Search(Daily(2024));

            result.Items.Select(i => i.CandidateId).Should().Equal("d-0517", "d-1231");
            result.AirDates.Should().Equal(new Dictionary<string, string> { { "d-0517", "2024-05-17" }, { "d-1231", "2024-12-30" } });
        }

        [Test]
        public void a_season_that_really_is_numbered_by_year_should_be_found_by_number()
        {
            GivenAired(_alice, "y-1", 2024, "1", "2024-03-01");

            var result = Subject.Search(Daily(2024));

            result.Items.Select(i => i.CandidateId).Should().Equal("y-1");
            result.AirDates.Should().BeEmpty();
        }

        [Test]
        public void a_search_by_number_should_still_find_a_show_that_others_search_by_day()
        {
            GivenDailyShow();

            var result = Subject.Search(Daily(29, "45"));

            result.Items.Select(i => i.CandidateId).Should().Equal("d-0517");
            result.AirDates.Should().BeEmpty();
        }

        [Test]
        public void an_episode_that_is_neither_a_number_nor_a_day_should_find_nothing()
        {
            GivenDailyShow();

            Found(Daily(29, "abc")).Should().BeEmpty();
        }

        [Test]
        public void a_search_with_no_ids_and_no_text_should_list_the_newest_first()
        {
            var result = Subject.Search(new CandidateQuery { Limit = 3 });

            result.Items.Select(i => i.CandidateId).Should().Equal("a-night", "b-e1", "a-s2e1");
            result.Total.Should().Be(7);

            Found(new CandidateQuery { Limit = 3, Offset = 3 }).Should().Equal("a-e45", "a-e2", "a-e1");
        }

        [Test]
        public void a_listing_should_keep_to_the_kind_asked_for()
        {
            Found(new CandidateQuery { IncludeTv = false }).Should().Equal("a-night");
            Found(new CandidateQuery { IncludeTv = false, IncludeMovies = false }).Should().BeEmpty();
        }

        [Test]
        public void a_text_search_should_need_every_word()
        {
            Found(new CandidateQuery { Text = "night living dead 1968" }).Should().Equal("a-night");
            Found(new CandidateQuery { Text = "Pingu S02E01" }).Should().Equal("a-s2e1");
            Found(new CandidateQuery { Text = "night of the living pingu" }).Should().BeEmpty();
        }

        [Test]
        public void results_should_be_paged()
        {
            var first = Subject.Search(new CandidateQuery { IncludeMovies = false, TvdbId = PinguTvdbId, Limit = 2 });
            var second = Subject.Search(new CandidateQuery { IncludeMovies = false, TvdbId = PinguTvdbId, Limit = 2, Offset = 2 });

            first.Total.Should().Be(5);
            first.Items.Select(i => i.CandidateId).Should().Equal("b-e1", "a-e1");
            second.Items.Select(i => i.CandidateId).Should().Equal("a-e2", "a-e45");
        }

        [Test]
        public void a_candidate_should_be_found_by_its_id_only_while_its_peer_is_trusted()
        {
            Subject.Find("a-e1").RemoteId.Should().Be("a-e1");
            Subject.Find("m-e1").Should().BeNull();
            Subject.Find("made-up").Should().BeNull();
            Subject.Find(null).Should().BeNull();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Sharing
{
    [TestFixture]
    public class SharingServiceFixture : DbTest<SharingService, ShareList>
    {
        private static readonly ShareKey Pingu = new(MediaKind.Tv, 1, 10);
        private static readonly ShareKey Bluey = new(MediaKind.Tv, 1, 11);
        private static readonly ShareKey NightOfTheLivingDead = new(MediaKind.Movie, 2, 20);

        private Peer _bob;
        private Peer _carol;
        private Peer _dave;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IShareListRepository>(Mocker.Resolve<ShareListRepository>());
            Mocker.SetConstant<IShareListItemRepository>(Mocker.Resolve<ShareListItemRepository>());
            Mocker.SetConstant<IShareListPeerRepository>(Mocker.Resolve<ShareListPeerRepository>());
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<ICatalogSeriesRepository>(Mocker.Resolve<CatalogSeriesRepository>());
            Mocker.SetConstant<ICatalogEpisodeRepository>(Mocker.Resolve<CatalogEpisodeRepository>());
            Mocker.SetConstant<ICatalogEpisodeFileRepository>(Mocker.Resolve<CatalogEpisodeFileRepository>());
            Mocker.SetConstant<ICatalogMovieRepository>(Mocker.Resolve<CatalogMovieRepository>());
            Mocker.SetConstant<ICatalogMovieFileRepository>(Mocker.Resolve<CatalogMovieFileRepository>());
            Mocker.SetConstant<ICatalogArtistRepository>(Mocker.Resolve<CatalogArtistRepository>());
            Mocker.SetConstant<ICatalogAlbumRepository>(Mocker.Resolve<CatalogAlbumRepository>());
            Mocker.SetConstant<ICatalogTrackFileRepository>(Mocker.Resolve<CatalogTrackFileRepository>());
            Mocker.SetConstant<IFileHashRepository>(Mocker.Resolve<FileHashRepository>());

            Mocker.GetMock<IApplicationFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<ApplicationDefinition> { new() { Id = 1, Name = "Sonarr" }, new() { Id = 2, Name = "Radarr" } });

            var series = Mocker.Resolve<ICatalogSeriesRepository>();
            var episodes = Mocker.Resolve<ICatalogEpisodeRepository>();
            var episodeFiles = Mocker.Resolve<ICatalogEpisodeFileRepository>();
            var movies = Mocker.Resolve<ICatalogMovieRepository>();
            var movieFiles = Mocker.Resolve<ICatalogMovieFileRepository>();

            series.Insert(new CatalogSeries { ApplicationId = 1, ArrId = 10, TvdbId = 75669, Title = "Pingu", Year = 1989, Path = "/tv/Pingu" });
            series.Insert(new CatalogSeries { ApplicationId = 1, ArrId = 11, TvdbId = 353546, Title = "Bluey", Year = 2018, Path = "/tv/Bluey" });

            // Pingu: one single-episode file and one file that holds episodes 4 and 5.
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 101, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, Title = "Hello, Pingu", ArrEpisodeFileId = 501 });
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 104, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 4, Title = "The New Arrival", AirDate = "1986-06-18", ArrEpisodeFileId = 502 });
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 105, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 5, Title = "Pingu Goes Fishing", ArrEpisodeFileId = 502 });
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 201, ArrSeriesId = 11, SeasonNumber = 1, EpisodeNumber = 1, Title = "Magic Xylophone", ArrEpisodeFileId = 601 });

            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 501, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/Season 1/Pingu - S01E01.mkv", RelativePath = "Season 1/Pingu - S01E01.mkv", Size = 100, Quality = "WEBDL-720p", Resolution = 720 });
            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 502, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/Season 1/Pingu - S01E04-E05.mkv", RelativePath = "Season 1/Pingu - S01E04-E05.mkv", SceneName = "Pingu.S01E04E05.720p.WEB-DL-GROUP", Size = 200, Quality = "WEBDL-720p", Resolution = 720 });
            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 601, ArrSeriesId = 11, SeasonNumber = 1, Path = "/tv/Bluey/Season 1/Bluey - S01E01.mkv", RelativePath = "Season 1\\Bluey - S01E01.mkv", Size = 300, Quality = "Bluray-1080p", Resolution = 1080 });

            movies.Insert(new CatalogMovie { ApplicationId = 2, ArrId = 20, TmdbId = 10331, ImdbId = "tt0063350", Title = "Night of the Living Dead", Year = 1968, ArrMovieFileId = 701 });
            movies.Insert(new CatalogMovie { ApplicationId = 2, ArrId = 21, TmdbId = 10513, Title = "Plan 9 from Outer Space", Year = 1957, ArrMovieFileId = 702 });

            movieFiles.Insert(new CatalogMovieFile { ApplicationId = 2, ArrId = 701, ArrMovieId = 20, Path = "/movies/Night of the Living Dead (1968)/movie.mkv", RelativePath = "movie.mkv", Size = 400, Quality = "WEBDL-720p", Resolution = 720 });

            // Radarr stopped reporting this one.
            movieFiles.Insert(new CatalogMovieFile { ApplicationId = 2, ArrId = 702, ArrMovieId = 21, Path = "/movies/Plan 9/movie.mkv", RelativePath = "movie.mkv", Size = 500, MissingSince = DateTime.UtcNow });

            _bob = GivenPeer("Bob", PeerState.Trusted);
            _carol = GivenPeer("Carol", PeerState.Trusted);
            _dave = GivenPeer("Dave", PeerState.Revoked);
        }

        private Peer GivenPeer(string name, PeerState state)
        {
            return Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = name.ToLowerInvariant(), Name = name, State = state, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });
        }

        private ShareListDetails Hidden => Subject.All().Single(l => l.List.IsHidden);

        private List<string> OfferedTo(Peer peer)
        {
            return Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(peer.Id), true).Items.Select(i => i.Id).ToList();
        }

        [Test]
        public void a_trusted_peer_should_be_offered_nothing_by_default()
        {
            OfferedTo(_bob).Should().BeEmpty();
        }

        [Test]
        public void share_everything_should_offer_every_file_the_arr_apps_still_report()
        {
            Subject.SetShareAll(_bob.Id, true);

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502", "e-1-601", "m-2-701");
            OfferedTo(_carol).Should().BeEmpty();
        }

        [Test]
        public void share_everything_should_leave_out_the_apps_left_out_for_the_peer()
        {
            Subject.SetShareAll(_bob.Id, true, new[] { 2 });
            Subject.SetShareAll(_carol.Id, true, new[] { 1 });

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502", "e-1-601");
            OfferedTo(_carol).Should().BeEquivalentTo("m-2-701");
        }

        [Test]
        public void a_list_should_still_share_from_an_app_left_out_of_everything()
        {
            var list = Subject.Create("Films for Bob");

            Subject.SetShareAll(_bob.Id, true, new[] { 2 });
            Subject.Update(list.List.Id, "Films for Bob", new[] { NightOfTheLivingDead }, new[] { _bob.Id });

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502", "e-1-601", "m-2-701");

            Subject.Update(Hidden.List.Id, null, new[] { NightOfTheLivingDead }, null);

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502", "e-1-601");
        }

        [Test]
        public void apps_left_out_should_be_kept_only_when_connected_and_stay_as_they_are_when_not_given()
        {
            Subject.SetShareAll(_bob.Id, true, new[] { 2, 99, 2 }).ExcludedApplicationIds.Should().Equal(2);
            Subject.SetShareAll(_bob.Id, false).ExcludedApplicationIds.Should().Equal(2);

            OfferedTo(_bob).Should().BeEmpty();
        }

        [Test]
        public void the_hidden_list_should_override_share_everything()
        {
            Subject.SetShareAll(_bob.Id, true);
            Subject.Update(Hidden.List.Id, null, new[] { Bluey, NightOfTheLivingDead }, null);

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502");
        }

        [Test]
        public void a_poster_should_be_given_for_an_offered_file_and_name_its_title()
        {
            var list = Subject.Create("Kids");

            Subject.Update(list.List.Id, "Kids", new[] { Pingu, NightOfTheLivingDead }, new[] { _bob.Id });

            Subject.PosterFor(_bob, "e-1-502").Should().Be(new PosterTitle(1, PosterSubject.Series, 10));
            Subject.PosterFor(_bob, "m-2-701").Should().Be(new PosterTitle(2, PosterSubject.Movie, 20));
        }

        [Test]
        public void no_poster_should_be_given_for_what_is_not_offered()
        {
            var list = Subject.Create("Kids");

            Subject.Update(list.List.Id, "Kids", new[] { Pingu }, new[] { _bob.Id });

            Subject.PosterFor(_carol, "e-1-501").Should().BeNull();
            Subject.PosterFor(_bob, "e-1-601").Should().BeNull();
            Subject.PosterFor(_bob, "e-1-0501").Should().BeNull();
            Subject.PosterFor(_bob, "../e-1-501").Should().BeNull();
        }

        [Test]
        public void a_share_list_should_offer_only_its_items_and_only_to_its_peers()
        {
            var list = Subject.Create("Kids");

            Subject.Update(list.List.Id, "Kids", new[] { Pingu }, new[] { _bob.Id });

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502");
            OfferedTo(_carol).Should().BeEmpty();
        }

        [Test]
        public void the_hidden_list_should_override_a_share_list()
        {
            var list = Subject.Create("Everything");

            Subject.Update(list.List.Id, null, new[] { Pingu, Bluey, NightOfTheLivingDead }, new[] { _bob.Id });
            Subject.Update(Hidden.List.Id, null, new[] { Pingu }, null);

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-601", "m-2-701");
        }

        [Test]
        public void a_peer_that_is_not_trusted_should_be_offered_nothing_whatever_is_shared()
        {
            var list = Subject.Create("Everything");

            Subject.Update(list.List.Id, null, new[] { Pingu, Bluey }, new[] { _dave.Id });
            Subject.SetShareAll(_dave.Id, true);

            OfferedTo(_dave).Should().BeEmpty();
        }

        [Test]
        public void the_hidden_list_should_never_be_given_to_a_peer()
        {
            Subject.Update(Hidden.List.Id, "Renamed", new[] { Pingu }, new[] { _bob.Id });

            Hidden.PeerIds.Should().BeEmpty();
            Hidden.List.Name.Should().Be(SharingService.HiddenListName);
            OfferedTo(_bob).Should().BeEmpty();
        }

        [Test]
        public void the_hidden_list_should_not_be_deletable()
        {
            Assert.Throws<InvalidOperationException>(() => Subject.Delete(Hidden.List.Id));
        }

        [Test]
        public void an_offer_should_say_what_a_file_is_and_not_where_it_is()
        {
            Subject.SetShareAll(_bob.Id, true);

            var offer = Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(_bob.Id), true);
            var json = JsonSerializer.Serialize(offer);

            // Not the folders, and not the file names either. Only the extension is sent.
            json.Should().NotContain("/tv/").And.NotContain("/movies/").And.NotContain("Season 1").And.NotContain("Pingu - S01E01");

            var pingu = offer.Items.Single(i => i.Id == "e-1-501");

            pingu.Should().Match<OfferItem>(i => i.Title == "Pingu" && i.TvdbId == 75669 && i.SeasonNumber == 1 && i.Quality == "WEBDL-720p" && i.Size == 100);
            pingu.SceneName.Should().BeNull();

            // The name the release was grabbed under is sent when the Arr app kept it.
            offer.Items.Single(i => i.Id == "e-1-502").SceneName.Should().Be("Pingu.S01E04E05.720p.WEB-DL-GROUP");
        }

        [Test]
        public void an_offer_should_say_what_kind_of_file_it_is_and_nothing_else_of_its_name()
        {
            Subject.SetShareAll(_bob.Id, true);

            var offer = Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(_bob.Id), true);

            offer.Items.Should().OnlyContain(i => i.Extension == ".mkv");
        }

        [Test]
        public void a_file_should_count_as_offered_only_to_a_peer_whose_offer_holds_it_now()
        {
            var list = Subject.Create("Kids");

            Subject.Update(list.List.Id, "Kids", new[] { Pingu }, new[] { _bob.Id });

            var bob = Mocker.Resolve<IPeerRepository>().Get(_bob.Id);

            Subject.IsOffered(bob, "e-1-501").Should().BeTrue();
            Subject.IsOffered(bob, "e-1-601").Should().BeFalse();
            Subject.IsOffered(bob, "m-2-701").Should().BeFalse();
            Subject.IsOffered(bob, "e-1-999").Should().BeFalse();
            Subject.IsOffered(bob, null).Should().BeFalse();
            Subject.IsOffered(Mocker.Resolve<IPeerRepository>().Get(_carol.Id), "e-1-501").Should().BeFalse();
            Subject.IsOffered(Mocker.Resolve<IPeerRepository>().Get(_dave.Id), "e-1-501").Should().BeFalse();

            // Hidden afterwards: the name Bob was given earlier earns nothing now.
            Subject.Update(Hidden.List.Id, null, new[] { Pingu }, null);
            Subject.IsOffered(bob, "e-1-501").Should().BeFalse();
        }

        // IsOffered answers from a file's own rows. It must agree with the offer itself, file for
        // file, whatever the sharing and whatever state the catalog is in.
        [Test]
        public void checking_one_file_should_always_agree_with_the_whole_offer()
        {
            // A file Sonarr has not tied to an episode, a series Sonarr stopped reporting with a
            // file still listed, and a movie whose file is still there but whose movie is not.
            Mocker.Resolve<ICatalogEpisodeFileRepository>().Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 503, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/extra.mkv", RelativePath = "extra.mkv", Size = 1 });
            Mocker.Resolve<ICatalogSeriesRepository>().Insert(new CatalogSeries { ApplicationId = 1, ArrId = 12, TvdbId = 1, Title = "Gone", Path = "/tv/Gone", MissingSince = DateTime.UtcNow });
            Mocker.Resolve<ICatalogEpisodeRepository>().Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 301, ArrSeriesId = 12, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 801 });
            Mocker.Resolve<ICatalogEpisodeFileRepository>().Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 801, ArrSeriesId = 12, SeasonNumber = 1, Path = "/tv/Gone/e1.mkv", RelativePath = "e1.mkv", Size = 1 });
            Mocker.Resolve<ICatalogMovieRepository>().Insert(new CatalogMovie { ApplicationId = 2, ArrId = 22, TmdbId = 2, Title = "Gone Movie", ArrMovieFileId = 703, MissingSince = DateTime.UtcNow });
            Mocker.Resolve<ICatalogMovieFileRepository>().Insert(new CatalogMovieFile { ApplicationId = 2, ArrId = 703, ArrMovieId = 22, Path = "/movies/gone.mkv", RelativePath = "gone.mkv", Size = 1 });

            var names = new[]
            {
                "e-1-501", "e-1-502", "e-1-503", "e-1-601", "e-1-801", "m-2-701", "m-2-702", "m-2-703",
                "m-1-501", "e-2-701", "e-1-999", "e-01-501", "e-1-501-1", "x-1-501", "e-1", "", null
            };

            void AllAgree(string setup)
            {
                foreach (var peer in Mocker.Resolve<IPeerRepository>().All())
                {
                    var offered = Subject.BuildOffer(peer, true).Items.Select(i => i.Id).ToHashSet();

                    foreach (var name in names)
                    {
                        Subject.IsOffered(peer, name).Should().Be(name != null && offered.Contains(name), $"{name} for {peer.Name} with {setup}");
                    }
                }
            }

            AllAgree("nothing shared");

            Subject.SetShareAll(_bob.Id, true);
            Subject.SetShareAll(_dave.Id, true);
            AllAgree("everything shared with Bob, and with Dave, who is not trusted");

            var list = Subject.Create("Kids");
            Subject.Update(list.List.Id, "Kids", new[] { Bluey, NightOfTheLivingDead, ShareKey.Series(1, 12) }, new[] { _carol.Id });
            AllAgree("a list for Carol");

            Subject.Update(Hidden.List.Id, null, new[] { Pingu, NightOfTheLivingDead }, null);
            AllAgree("Pingu and the movie hidden");

            Subject.Update(list.List.Id, "Kids", new[] { ShareKey.Season(1, 10, 1), ShareKey.Episode(1, 11, 201) }, new[] { _carol.Id });
            Subject.Update(Hidden.List.Id, null, new[] { ShareKey.Episode(1, 10, 105) }, null);
            AllAgree("a season and an episode for Carol, and one episode of a two-episode file hidden");

            Subject.Update(Hidden.List.Id, null, Array.Empty<ShareKey>(), null);
            Subject.Update(list.List.Id, "Kids", new[] { ShareKey.Episode(1, 10, 104) }, new[] { _carol.Id });
            AllAgree("one episode of a two-episode file for Carol");
        }

        // Pingu also gets a second season: 2x01 in a file of its own.
        private void GivenSecondSeason()
        {
            Mocker.Resolve<ICatalogEpisodeRepository>().Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 110, ArrSeriesId = 10, SeasonNumber = 2, EpisodeNumber = 1, Title = "Pingu and the Snowball", ArrEpisodeFileId = 503 });
            Mocker.Resolve<ICatalogEpisodeFileRepository>().Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 503, ArrSeriesId = 10, SeasonNumber = 2, Path = "/tv/Pingu/Season 2/Pingu - S02E01.mkv", RelativePath = "Season 2/Pingu - S02E01.mkv", Size = 100 });
        }

        private void GivenListForBob(params ShareKey[] items)
        {
            var list = Subject.Create("Picked");

            Subject.Update(list.List.Id, "Picked", items, new[] { _bob.Id });
        }

        [Test]
        public void a_season_should_share_that_seasons_files_only()
        {
            GivenSecondSeason();
            GivenListForBob(ShareKey.Season(1, 10, 1));

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-502");
        }

        [Test]
        public void a_season_should_take_in_episodes_that_arrive_later_as_a_whole_series_does()
        {
            GivenListForBob(ShareKey.Season(1, 10, 1));

            Mocker.Resolve<ICatalogEpisodeRepository>().Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 106, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 6, ArrEpisodeFileId = 504 });
            Mocker.Resolve<ICatalogEpisodeFileRepository>().Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 504, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/e6.mkv", RelativePath = "e6.mkv", Size = 1 });

            OfferedTo(_bob).Should().Contain("e-1-504");
        }

        [Test]
        public void a_single_episode_should_share_its_file_only()
        {
            GivenSecondSeason();
            GivenListForBob(ShareKey.Episode(1, 10, 110));

            OfferedTo(_bob).Should().Equal("e-1-503");
        }

        // Sonarr wants a release only when every episode in it is monitored.
        [Test]
        public void a_file_of_two_episodes_should_be_shared_only_when_both_are()
        {
            GivenListForBob(ShareKey.Episode(1, 10, 104));
            OfferedTo(_bob).Should().BeEmpty();

            GivenListForBob(ShareKey.Episode(1, 10, 105));
            OfferedTo(_bob).Should().Equal("e-1-502");
        }

        [Test]
        public void hiding_one_episode_should_keep_back_every_file_it_is_in_and_nothing_else()
        {
            Subject.SetShareAll(_bob.Id, true);
            Subject.Update(Hidden.List.Id, null, new[] { ShareKey.Episode(1, 10, 105) }, null);

            OfferedTo(_bob).Should().BeEquivalentTo("e-1-501", "e-1-601", "m-2-701");
        }

        [Test]
        public void hiding_a_season_should_keep_back_that_season_whatever_else_shares_it()
        {
            GivenSecondSeason();
            GivenListForBob(Pingu);
            Subject.Update(Hidden.List.Id, null, new[] { ShareKey.Season(1, 10, 1) }, null);

            OfferedTo(_bob).Should().Equal("e-1-503");
        }

        [Test]
        public void a_list_should_keep_a_season_or_episode_only_where_the_whole_series_is_not_on_it()
        {
            var list = Subject.Create("Mixed");

            var items = new[]
            {
                Pingu, ShareKey.Season(1, 10, 1), ShareKey.Episode(1, 10, 101),
                ShareKey.Season(1, 11, 1), ShareKey.Episode(1, 11, 201),
                new ShareKey(MediaKind.Movie, 2, 20, 1, 5),
                ShareKey.Season(1, 11, -1), new ShareKey(MediaKind.Tv, 1, 11, 1, 201)
            };

            var saved = Subject.Update(list.List.Id, "Mixed", items, null);

            saved.Items.Should().BeEquivalentTo(new[] { Pingu, ShareKey.Season(1, 11, 1), ShareKey.Episode(1, 11, 201), NightOfTheLivingDead });
        }

        [Test]
        public void the_seasons_of_a_series_should_list_its_episodes_and_which_have_files()
        {
            GivenSecondSeason();
            Mocker.Resolve<ICatalogEpisodeRepository>().Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 111, ArrSeriesId = 10, SeasonNumber = 2, EpisodeNumber = 2, Title = "Not yet" });

            var seasons = Subject.GetSeasons(1, 10);

            seasons.Select(s => s.SeasonNumber).Should().Equal(1, 2);
            seasons[0].Episodes.Select(e => e.EpisodeNumber).Should().Equal(1, 4, 5);
            seasons[1].Episodes.Select(e => (e.EpisodeArrId, e.EpisodeNumber, e.Title, e.HasFile)).Should().Equal((110, 1, "Pingu and the Snowball", true), (111, 2, "Not yet", false));
        }

        [Test]
        public void a_file_should_carry_the_scenes_numbers_only_where_they_differ_from_tvdbs()
        {
            // Pingu's 1x04 and 1x05 are the scene's 2x01 and 2x02. Bluey's 1x01 has scene numbers
            // that are TVDB's own.
            var episodes = Mocker.Resolve<ICatalogEpisodeRepository>();
            var all = episodes.All().ToList();

            foreach (var episode in all.Where(e => e.ArrEpisodeFileId == 502))
            {
                episode.SceneSeasonNumber = 2;
                episode.SceneEpisodeNumber = episode.EpisodeNumber - 3;
            }

            var bluey = all.Single(e => e.ArrId == 201);
            bluey.SceneSeasonNumber = 1;
            bluey.SceneEpisodeNumber = 1;
            episodes.UpdateMany(all);

            Subject.SetShareAll(_bob.Id, true);

            var items = Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(_bob.Id), true).Items;
            var multi = items.Single(i => i.Id == "e-1-502");

            multi.SeasonNumber.Should().Be(1);
            multi.EpisodeNumbers.Should().Equal(4, 5);
            multi.SceneSeasonNumber.Should().Be(2);
            multi.SceneEpisodeNumbers.Should().Equal(1, 2);

            items.Where(i => i.Id != "e-1-502").Should().OnlyContain(i => i.SceneSeasonNumber == null && i.SceneEpisodeNumbers == null);

            // Left out of what is sent, so an offer with no scene numbers is the same as before.
            var json = JsonSerializer.Serialize(items.Single(i => i.Id == "e-1-601"));

            json.Should().NotContain("Scene" + "SeasonNumber").And.NotContain("SceneEpisodeNumbers");
        }

        [Test]
        public void a_file_should_carry_its_hash_where_one_was_worked_out_for_a_file_its_size()
        {
            var hashes = Mocker.Resolve<IFileHashRepository>();

            hashes.Insert(new FileHash { Path = "/tv/a.mkv", OfferFileId = "e-1-501", Size = 100, ModifiedNs = 1, Blake3 = new string('a', 64), Hashed = DateTime.UtcNow });
            hashes.Insert(new FileHash { Path = "/tv/b.mkv", OfferFileId = "e-1-601", Size = 999, ModifiedNs = 1, Blake3 = new string('b', 64), Hashed = DateTime.UtcNow });
            Subject.SetShareAll(_bob.Id, true);

            var items = Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(_bob.Id), true).Items;

            items.Single(i => i.Id == "e-1-501").Blake3.Should().Be(new string('a', 64));

            // Worked out for a file of another size, so not this file as the Arr app has it now.
            items.Single(i => i.Id == "e-1-601").Blake3.Should().BeNull();
            items.Single(i => i.Id == "e-1-502").Blake3.Should().BeNull();
            items.Should().OnlyContain(i => i.Fingerprint != null && i.Fingerprint.Length == 16);
        }

        [Test]
        public void a_multi_episode_file_should_be_offered_once_with_all_its_episodes()
        {
            Subject.SetShareAll(_bob.Id, true);

            var item = Subject.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(_bob.Id), true).Items.Single(i => i.Id == "e-1-502");

            item.EpisodeNumbers.Should().Equal(4, 5);
            item.EpisodeTitles.Should().Equal("The New Arrival", "Pingu Goes Fishing");

            // The day each episode aired, where the Arr app knows it. One of the two has none here.
            item.AirDates.Should().Equal("1986-06-18");
        }

        [Test]
        public void the_version_should_change_only_when_the_offer_does()
        {
            Subject.SetShareAll(_bob.Id, true);

            var peer = Mocker.Resolve<IPeerRepository>().Get(_bob.Id);
            var first = Subject.BuildOffer(peer, true).Version;

            Subject.BuildOffer(peer, true).Version.Should().Be(first);

            Subject.Update(Hidden.List.Id, null, new[] { Bluey }, null);

            Subject.BuildOffer(peer, true).Version.Should().NotBe(first);
        }

        [Test]
        public void changing_what_is_shared_should_be_announced()
        {
            Subject.SetShareAll(_bob.Id, true);

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<SharingChangedEvent>()), Times.Once());
        }

        [Test]
        public void removing_an_application_should_remove_its_titles_from_every_list()
        {
            var list = Subject.Create("Mixed");

            Subject.Update(list.List.Id, null, new[] { Pingu, NightOfTheLivingDead }, new[] { _bob.Id });
            Subject.Handle(new ProviderDeletedEvent<IApplication>(1));

            Subject.All().Single(l => l.List.Id == list.List.Id).Items.Should().Equal(NightOfTheLivingDead);
        }

        [Test]
        public void deleting_a_peer_should_take_it_off_every_list()
        {
            var list = Subject.Create("Kids");

            Subject.Update(list.List.Id, null, new[] { Pingu }, new[] { _bob.Id, _carol.Id });
            Subject.Handle(new PeerDeletedEvent(_bob.Id));

            Subject.All().Single(l => l.List.Id == list.List.Id).PeerIds.Should().Equal(_carol.Id);
        }

        [Test]
        public void titles_should_list_what_can_be_shared()
        {
            var titles = Subject.GetTitles();

            titles.Select(t => t.Title).Should().Equal("Bluey", "Night of the Living Dead", "Pingu", "Plan 9 from Outer Space");
            titles.Single(t => t.Key == Pingu).Should().Match<ShareableTitle>(t => t.Files == 2 && t.ApplicationName == "Sonarr");
            titles.Single(t => t.Title == "Plan 9 from Outer Space").Files.Should().Be(0);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Sharing
{
    // Sending something to a friend shares it on a list of their own, and tells their node.
    [TestFixture]
    public class SendServiceFixture : DbTest<SendService, SentTitle>
    {
        private static readonly ShareKey Pingu = ShareKey.Series(1, 10);
        private static readonly ShareKey PlanNine = ShareKey.Movie(2, 21);
        private static readonly ShareKey Low = ShareKey.Artist(3, 7);

        private Peer _bob;
        private Peer _carol;
        private List<PeerMessage> _told;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IShareListRepository>(Mocker.Resolve<ShareListRepository>());
            Mocker.SetConstant<IShareListItemRepository>(Mocker.Resolve<ShareListItemRepository>());
            Mocker.SetConstant<IShareListPeerRepository>(Mocker.Resolve<ShareListPeerRepository>());
            Mocker.SetConstant<ISentTitleRepository>(Mocker.Resolve<SentTitleRepository>());
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
            Mocker.SetConstant<ISharingService>(Mocker.Resolve<SharingService>());

            Mocker.GetMock<IApplicationFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<ApplicationDefinition> { new() { Id = 1, Name = "Sonarr" }, new() { Id = 2, Name = "Radarr" }, new() { Id = 3, Name = "Lidarr" } });

            Mocker.Resolve<ICatalogSeriesRepository>().Insert(new CatalogSeries { ApplicationId = 1, ArrId = 10, TvdbId = 75669, Title = "Pingu", Year = 1989, Path = "/tv/Pingu" });

            var episodes = Mocker.Resolve<ICatalogEpisodeRepository>();

            // Pingu: two episodes of season 1 and one of season 2, each in a file of its own.
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 101, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1, ArrEpisodeFileId = 501 });
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 104, ArrSeriesId = 10, SeasonNumber = 1, EpisodeNumber = 4, ArrEpisodeFileId = 502 });
            episodes.Insert(new CatalogEpisode { ApplicationId = 1, ArrId = 201, ArrSeriesId = 10, SeasonNumber = 2, EpisodeNumber = 1, ArrEpisodeFileId = 503 });

            var episodeFiles = Mocker.Resolve<ICatalogEpisodeFileRepository>();

            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 501, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/1.mkv", RelativePath = "1.mkv", Size = 1 });
            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 502, ArrSeriesId = 10, SeasonNumber = 1, Path = "/tv/Pingu/4.mkv", RelativePath = "4.mkv", Size = 1 });
            episodeFiles.Insert(new CatalogEpisodeFile { ApplicationId = 1, ArrId = 503, ArrSeriesId = 10, SeasonNumber = 2, Path = "/tv/Pingu/2-1.mkv", RelativePath = "2-1.mkv", Size = 1 });

            // A movie Radarr has no file for.
            Mocker.Resolve<ICatalogMovieRepository>().Insert(new CatalogMovie { ApplicationId = 2, ArrId = 21, TmdbId = 10513, Title = "Plan 9 from Outer Space", Year = 1957 });

            Mocker.Resolve<ICatalogArtistRepository>().Insert(new CatalogArtist { ApplicationId = 3, ArrId = 7, ForeignArtistId = "low-mbid", Name = "Low", Path = "/music/Low" });

            var albums = Mocker.Resolve<ICatalogAlbumRepository>();

            albums.Insert(new CatalogAlbum { ApplicationId = 3, ArrId = 70, ArrArtistId = 7, ForeignAlbumId = "things-we-lost", Title = "Things We Lost in the Fire", TrackCount = 1, TrackFileCount = 1 });
            albums.Insert(new CatalogAlbum { ApplicationId = 3, ArrId = 72, ArrArtistId = 7, ForeignAlbumId = "trust", Title = "Trust", TrackCount = 1, TrackFileCount = 1 });

            var tracks = Mocker.Resolve<ICatalogTrackFileRepository>();

            tracks.Insert(new CatalogTrackFile { ApplicationId = 3, ArrId = 701, ArrArtistId = 7, ArrAlbumId = 70, Path = "/music/Low/a/01.flac", RelativePath = "a/01.flac", Size = 1 });
            tracks.Insert(new CatalogTrackFile { ApplicationId = 3, ArrId = 721, ArrArtistId = 7, ArrAlbumId = 72, Path = "/music/Low/b/01.flac", RelativePath = "b/01.flac", Size = 1 });

            var peers = Mocker.Resolve<IPeerRepository>();

            _bob = peers.Insert(new Peer { NodeId = "bob", Name = "Bob", State = PeerState.Trusted, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });
            _carol = peers.Insert(new Peer { NodeId = "carol", Name = "Carol", State = PeerState.Trusted, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });

            _told = new List<PeerMessage>();

            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()))
                  .Callback<Peer, PeerMessage>((_, m) => _told.Add(m))
                  .Returns(new PeerReply { Ok = true });

            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.CreateInvite(It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(new CreatedInvite { Invite = new PeerInvite { Id = 9, Expires = DateTime.UtcNow.AddDays(3) }, Code = "nodearr-invite" });

            Subject.PairingSettle = TimeSpan.Zero;
        }

        private ISharingService Sharing => Mocker.Resolve<ISharingService>();

        private Peer Bob => Mocker.Resolve<IPeerRepository>().Get(_bob.Id);

        private SentTitle Reload(SentTitle sent) => Mocker.Resolve<ISentTitleRepository>().Get(sent.Id);

        private SentTitle SendAndDeliver(Peer peer, params ShareKey[] items)
        {
            var sent = Subject.Send(peer.Id, items);

            Subject.HandleAsync(new SendQueuedEvent(sent.Id));

            return Reload(sent);
        }

        [Test]
        public void sending_should_share_it_on_the_friends_own_list()
        {
            Subject.Send(_bob.Id, new[] { Pingu });

            var list = Sharing.All().Single(l => l.List.SentToPeerId == _bob.Id);

            list.List.Name.Should().Be("Sent to Bob");
            list.Items.Should().BeEquivalentTo(new[] { Pingu });
            list.PeerIds.Should().BeEquivalentTo(new[] { _bob.Id });
            Sharing.BuildOffer(Bob, true).Items.Select(i => i.Id).Should().BeEquivalentTo("e-1-501", "e-1-502", "e-1-503");
        }

        [Test]
        public void a_second_send_should_go_on_the_same_list()
        {
            Subject.Send(_bob.Id, new[] { ShareKey.Season(1, 10, 2) });
            Subject.Send(_bob.Id, new[] { ShareKey.Album(3, 7, 70) });

            Sharing.All().Where(l => l.List.SentToPeerId == _bob.Id).Should().ContainSingle()
                   .Which.Items.Should().BeEquivalentTo(new[] { ShareKey.Season(1, 10, 2), ShareKey.Album(3, 7, 70) });
        }

        [Test]
        public void what_is_sent_to_one_friend_should_not_be_offered_to_another()
        {
            Subject.Send(_bob.Id, new[] { Pingu });

            Sharing.BuildOffer(Mocker.Resolve<IPeerRepository>().Get(_carol.Id), true).Items.Should().BeEmpty();
        }

        [Test]
        public void the_friend_should_be_told_the_title_and_its_files()
        {
            var sent = SendAndDeliver(_bob, Pingu);

            sent.DeliveredAt.Should().NotBeNull();
            sent.LastError.Should().BeNull();

            var told = _told.Single();

            told.Type.Should().Be(PeerMessageTypes.Sent);
            told.Sent.Should().Match<SentMessage>(m => m.SendId == sent.SendId && m.Title == "Pingu" && m.Summary == null && m.IsWhole && m.MediaKind == MediaKind.Tv);
            told.Sent.OfferIds.Should().BeEquivalentTo("e-1-501", "e-1-502", "e-1-503");
        }

        [Test]
        public void the_note_should_say_what_the_arr_apps_know_it_by_and_which_parts()
        {
            SendAndDeliver(_bob, ShareKey.Season(1, 10, 2), ShareKey.Episode(1, 10, 104));

            var told = _told.Single().Sent;

            told.Should().Match<SentMessage>(m => m.TvdbId == 75669 && m.Year == 1989);
            told.Parts.Seasons.Should().Equal(2);
            told.Parts.Episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)).Should().Equal((1, 4));
        }

        [Test]
        public void the_note_for_albums_should_name_them_by_musicbrainz()
        {
            SendAndDeliver(_bob, ShareKey.Album(3, 7, 72));

            var told = _told.Single().Sent;

            told.ForeignArtistId.Should().Be("low-mbid");
            told.Parts.Albums.Should().Equal("trust");
        }

        [TestCase(new[] { 1 }, new int[0], "Season 1")]
        [TestCase(new[] { 1, 2 }, new int[0], "Seasons 1 and 2")]
        [TestCase(new int[0], new[] { 104 }, "S01E04")]
        [TestCase(new int[0], new[] { 101, 104 }, "2 episodes")]
        [TestCase(new[] { 2 }, new[] { 101 }, "Season 2, plus S01E01")]
        public void part_of_a_series_should_say_which_part(int[] seasons, int[] episodes, string summary)
        {
            var keys = seasons.Select(n => ShareKey.Season(1, 10, n)).Concat(episodes.Select(e => ShareKey.Episode(1, 10, e))).ToArray();

            var sent = SendAndDeliver(_bob, keys);

            sent.Summary.Should().Be(summary);
            _told.Single().Sent.IsWhole.Should().BeFalse();
        }

        [Test]
        public void albums_should_be_named_when_one_and_counted_when_more()
        {
            Subject.Send(_bob.Id, new[] { ShareKey.Album(3, 7, 70) }).Summary.Should().Be("Things We Lost in the Fire");
            Subject.Send(_bob.Id, new[] { ShareKey.Album(3, 7, 70), ShareKey.Album(3, 7, 72) }).Summary.Should().Be("2 albums");
            Subject.Send(_bob.Id, new[] { Low }).Summary.Should().BeNull();
        }

        [Test]
        public void only_one_title_should_be_sent_at_a_time()
        {
            Assert.Throws<SendException>(() => Subject.Send(_bob.Id, new[] { Pingu, Low }));
        }

        [Test]
        public void something_with_no_file_yet_should_not_be_sent()
        {
            var refused = Assert.Throws<SendException>(() => Subject.Send(_bob.Id, new[] { PlanNine }));

            refused.Message.Should().Contain("no file");
            Sharing.All().Should().NotContain(l => l.List.SentToPeerId == _bob.Id);
        }

        [Test]
        public void something_on_the_hidden_list_should_not_be_sent()
        {
            var hidden = Sharing.All().Single(l => l.List.IsHidden);

            Sharing.Update(hidden.List.Id, null, new[] { Pingu }, null);

            Assert.Throws<SendException>(() => Subject.Send(_bob.Id, new[] { ShareKey.Season(1, 10, 1) })).Message.Should().Contain("Hidden");
        }

        [Test]
        public void a_node_that_is_not_a_friend_should_not_be_sent_anything()
        {
            var peers = Mocker.Resolve<IPeerRepository>();
            var bob = Bob;

            bob.State = PeerState.Revoked;
            peers.Update(bob);

            Assert.Throws<SendException>(() => Subject.Send(_bob.Id, new[] { Pingu }));
        }

        [Test]
        public void a_note_with_no_direct_path_should_say_a_direct_connection_is_needed_and_be_kept()
        {
            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()))
                  .Throws(new TransportException(TransportFailure.NoDirectRoute, "no direct path within 20s"));

            var sent = SendAndDeliver(_bob, Pingu);

            sent.DeliveredAt.Should().BeNull();
            sent.LastError.Should().Be(TransportException.NoDirectRouteReason);
        }

        [Test]
        public void a_note_not_taken_should_be_kept_and_tried_again_at_the_next_connection_check()
        {
            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()))
                  .Throws(new TransportException(TransportFailure.NotAuthorized, "the peer refused the message"));

            var sent = SendAndDeliver(_bob, Pingu);

            sent.DeliveredAt.Should().BeNull();
            sent.LastError.Should().Contain("shared with them either way");

            Mocker.GetMock<IPeerService>()
                  .Setup(s => s.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>()))
                  .Returns(new PeerReply { Ok = true });

            Subject.HandleAsync(new PeerConnectionCheckedEvent(Bob));

            Reload(sent).DeliveredAt.Should().NotBeNull();
        }

        [Test]
        public void a_connection_check_that_did_not_reach_the_node_should_not_try()
        {
            var sent = Subject.Send(_bob.Id, new[] { Pingu });
            var bob = Bob;

            bob.LastError = "NoRoute: unreachable";
            Subject.HandleAsync(new PeerConnectionCheckedEvent(bob));

            _told.Should().BeEmpty();
            Reload(sent).DeliveredAt.Should().BeNull();
        }

        [Test]
        public void a_send_long_untold_should_stop_being_tried()
        {
            var sent = Subject.Send(_bob.Id, new[] { Pingu });
            var stored = Reload(sent);

            stored.Created = DateTime.UtcNow - SendService.GiveUpAfter - TimeSpan.FromDays(1);
            Mocker.Resolve<ISentTitleRepository>().Update(stored);

            Subject.HandleAsync(new PeerConnectionCheckedEvent(Bob));

            _told.Should().BeEmpty();
        }

        [Test]
        public void sending_to_someone_new_should_make_an_invite_that_shares_everything_but_hidden_by_default()
        {
            var result = Subject.SendWithInvite(new[] { Pingu }, TimeSpan.FromDays(3));

            result.Invite.Code.Should().Be("nodearr-invite");
            result.Sent.Should().Match<SentTitle>(s => s.InviteId == 9 && s.PeerId == null);
            Mocker.GetMock<IPeerService>().Verify(s => s.CreateInvite(TimeSpan.FromDays(3), "For Pingu", true), Times.Once());
            Sharing.All().Should().NotContain(l => l.List.SentToPeerId != null);
        }

        [Test]
        public void sending_to_someone_new_can_share_only_what_was_sent()
        {
            Subject.SendWithInvite(new[] { Pingu }, TimeSpan.FromDays(3), false);

            Mocker.GetMock<IPeerService>().Verify(s => s.CreateInvite(TimeSpan.FromDays(3), "For Pingu", false), Times.Once());
        }

        [Test]
        public void a_send_that_would_be_refused_should_make_no_invite()
        {
            Assert.Throws<SendException>(() => Subject.SendWithInvite(new[] { PlanNine }, TimeSpan.FromDays(3)));

            Mocker.GetMock<IPeerService>().Verify(s => s.CreateInvite(It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void using_the_invite_should_share_what_was_sent_and_tell_the_new_friend()
        {
            var result = Subject.SendWithInvite(new[] { Pingu }, TimeSpan.FromDays(3));

            Subject.Handle(new InviteRedeemedEvent(new PeerInvite { Id = 9 }, _carol));

            Reload(result.Sent).PeerId.Should().Be(_carol.Id);
            Sharing.All().Single(l => l.List.SentToPeerId == _carol.Id).List.Name.Should().Be("Sent to Carol");
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<SendQueuedEvent>(q => q.SentTitleId == result.Sent.Id && q.AfterPairing)), Times.Once());

            Subject.HandleAsync(new SendQueuedEvent(result.Sent.Id, true));

            Reload(result.Sent).DeliveredAt.Should().NotBeNull();
        }

        [Test]
        public void another_invite_being_used_should_change_nothing()
        {
            var result = Subject.SendWithInvite(new[] { Pingu }, TimeSpan.FromDays(3));

            Subject.Handle(new InviteRedeemedEvent(new PeerInvite { Id = 10 }, _carol));

            Reload(result.Sent).PeerId.Should().BeNull();
        }

        [Test]
        public void forgetting_a_send_with_an_unused_invite_should_delete_the_invite()
        {
            var result = Subject.SendWithInvite(new[] { Pingu }, TimeSpan.FromDays(3));

            Subject.Delete(result.Sent.Id);

            Mocker.GetMock<IPeerService>().Verify(s => s.DeleteInvite(9), Times.Once());
            Subject.All().Should().BeEmpty();
        }

        [Test]
        public void forgetting_a_send_should_leave_it_shared()
        {
            var sent = Subject.Send(_bob.Id, new[] { Pingu });

            Subject.Delete(sent.Id);

            Sharing.BuildOffer(Bob, true).Items.Should().HaveCount(3);
        }

        [Test]
        public void deleting_a_friend_should_delete_their_list_and_sends()
        {
            Subject.Send(_bob.Id, new[] { Pingu });
            Subject.Send(_carol.Id, new[] { Pingu });

            Subject.Handle(new PeerDeletedEvent(_bob.Id));

            Sharing.All().Should().NotContain(l => l.List.SentToPeerId == _bob.Id);
            Subject.All().Should().ContainSingle().Which.PeerId.Should().Be(_carol.Id);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Radarr;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Statistics
{
    [TestFixture]
    public class StatisticsRecorderFixture : DbTest<StatisticsRecorder, TransferStatistic>
    {
        private static readonly DateTime Afternoon = new(2026, 11, 3, 14, 37, 12, DateTimeKind.Utc);
        private static readonly DateTime AfternoonHour = new(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ITransferStatisticRepository>(Mocker.Resolve<TransferStatisticRepository>());
            Mocker.SetConstant<IConnectionStatisticRepository>(Mocker.Resolve<ConnectionStatisticRepository>());
            Mocker.SetConstant<IPeerRouteRepository>(Mocker.Resolve<PeerRouteRepository>());
            Mocker.SetConstant<IFetchedTitleRepository>(Mocker.Resolve<FetchedTitleRepository>());

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(new List<ApplicationDefinition>
            {
                new() { Id = 3, Name = "Sonarr", Settings = new SonarrSettings { DownloadCategory = "tv" } },
                new() { Id = 4, Name = "Radarr", Settings = new RadarrSettings { DownloadCategory = "movies" } }
            });

            Mocker.GetMock<IStatisticsTitles>().Setup(t => t.TitleOf(It.IsAny<string>())).Returns(((int, string)?)null);
            Mocker.GetMock<IStatisticsTitles>().Setup(t => t.TitleOf("e-3-501")).Returns((3, "series-3-12"));
        }

        private static TransferJob Download(TransferState state, TransferFailure failure = TransferFailure.None, string category = "tv")
        {
            return new TransferJob
            {
                DownloadId = "SABnzbd_nzo_1",
                PeerNodeId = "alice",
                Category = category,
                Size = 1000,
                BytesDone = state == TransferState.Completed ? 1000 : 300,
                BytesMoved = state == TransferState.Completed ? 1000 : 300,
                MovingMilliseconds = 4000,
                State = state,
                Failure = failure,
                Started = Afternoon.AddMinutes(-5),
                Ended = Afternoon
            };
        }

        private static Upload Upload(UploadState state, string failure = null, long sent = 1000)
        {
            return new Upload { Id = "7", PeerNodeId = "bob", File = "e-3-501", State = state, Failure = failure, Size = 1000, BytesSent = sent, Started = Afternoon.AddMinutes(-2), Ended = state is UploadState.Completed or UploadState.Failed ? Afternoon : null };
        }

        private List<TransferStatistic> Rows => AllStoredModels.ToList();

        [Test]
        public void a_download_should_be_counted_once_in_the_hour_it_finished_under_its_friend_app_and_direction()
        {
            Subject.Handle(new TransferEndedEvent(Download(TransferState.Completed)));

            Rows.Should().ContainSingle().Which.Should().Match<TransferStatistic>(s =>
                s.Hour == AfternoonHour && s.PeerNodeId == "alice" && s.ApplicationId == 3 && s.Direction == TransferDirection.Fetched && s.IsFinished &&
                s.Bytes == 1000 && s.Transfers == 1 && s.Seconds == 4 && s.PeakBytesPerSecond == 250);
        }

        [Test]
        public void downloads_in_the_same_hour_should_add_up_and_keep_the_fastest()
        {
            Subject.Handle(new TransferEndedEvent(Download(TransferState.Completed)));

            var faster = Download(TransferState.Completed);
            faster.MovingMilliseconds = 1000;
            Subject.Handle(new TransferEndedEvent(faster));

            Rows.Should().ContainSingle().Which.Should().Match<TransferStatistic>(s => s.Transfers == 2 && s.Bytes == 2000 && s.Seconds == 5 && s.PeakBytesPerSecond == 1000);
        }

        [Test]
        public void a_failed_download_should_be_counted_by_its_reason()
        {
            Subject.Handle(new TransferEndedEvent(Download(TransferState.Failed, TransferFailure.NotFound)));
            Subject.Handle(new TransferEndedEvent(Download(TransferState.Failed, TransferFailure.NoDirectRoute)));
            Subject.Handle(new TransferEndedEvent(Download(TransferState.Failed, TransferFailure.NotFound)));

            Rows.Should().HaveCount(2);
            Rows.Single(s => s.Failure == "NotFound").Should().Match<TransferStatistic>(s => s.Transfers == 2 && s.Bytes == 600 && !s.IsFinished && s.PeakBytesPerSecond == 0);
            Rows.Single(s => s.Failure == "NoDirectRoute").Transfers.Should().Be(1);
        }

        [Test]
        public void a_download_in_a_category_no_app_uses_should_be_counted_under_no_app()
        {
            Subject.Handle(new TransferEndedEvent(Download(TransferState.Completed, category: "anime")));

            Rows.Single().ApplicationId.Should().Be(0);
        }

        [Test]
        public void a_download_from_before_nodearr_counted_what_moved_should_count_what_it_holds_and_its_time()
        {
            var old = Download(TransferState.Completed);
            old.BytesMoved = 0;
            old.MovingMilliseconds = 0;

            Subject.Handle(new TransferEndedEvent(old));

            Rows.Single().Should().Match<TransferStatistic>(s => s.Bytes == 1000 && s.Seconds == 300);
        }

        [Test]
        public void a_sent_file_should_be_counted_under_its_app_with_only_what_moved_in_the_attempt()
        {
            Subject.Handle(new UploadAttemptEndedEvent(Upload(UploadState.Completed), 600, TimeSpan.FromSeconds(3)));

            Rows.Single().Should().Match<TransferStatistic>(s =>
                s.Hour == AfternoonHour && s.PeerNodeId == "bob" && s.ApplicationId == 3 && s.Direction == TransferDirection.Sent && s.IsFinished && s.Bytes == 600 && s.Transfers == 1 && s.PeakBytesPerSecond == 200);
        }

        [Test]
        public void an_upload_that_cannot_be_matched_to_a_catalog_entry_should_be_counted_under_no_app()
        {
            var upload = Upload(UploadState.Completed);
            upload.File = "e-3-999";

            Subject.Handle(new UploadRequestedEvent(upload));
            Subject.Handle(new UploadAttemptEndedEvent(upload, 1000, TimeSpan.FromSeconds(1)));

            Rows.Should().OnlyContain(s => s.ApplicationId == 0);
            Mocker.Resolve<IFetchedTitleRepository>().All().Should().BeEmpty();
        }

        [Test]
        public void an_upload_from_an_app_that_was_removed_should_be_counted_under_no_app()
        {
            Mocker.GetMock<IStatisticsTitles>().Setup(t => t.TitleOf("m-9-1")).Returns((9, "movie-9-1"));
            var upload = Upload(UploadState.Completed);
            upload.File = "m-9-1";

            Subject.Handle(new UploadAttemptEndedEvent(upload, 1000, TimeSpan.FromSeconds(1)));

            Rows.Single().ApplicationId.Should().Be(0);
        }

        [Test]
        public void an_upload_asked_for_again_after_it_failed_should_not_stay_counted_as_failed()
        {
            Subject.Handle(new UploadAttemptEndedEvent(Upload(UploadState.Failed, "DirectPathLost", 400), 400, TimeSpan.FromSeconds(2)));
            Subject.Handle(new UploadReopenedEvent(Upload(UploadState.Preparing, sent: 400), "DirectPathLost", Afternoon));
            Subject.Handle(new UploadAttemptEndedEvent(Upload(UploadState.Completed), 600, TimeSpan.FromSeconds(3)));

            Rows.Single(s => s.Failure == "DirectPathLost").Should().Match<TransferStatistic>(s => s.Counted == 0 && s.Bytes == 400);
            Rows.Single(s => s.IsFinished).Counted.Should().Be(1);
        }

        // A total that goes down reads in Prometheus as a restart (/metrics).
        [Test]
        public void taking_back_a_failure_should_not_lower_the_count()
        {
            Subject.Handle(new UploadAttemptEndedEvent(Upload(UploadState.Failed, "DirectPathLost", 400), 400, TimeSpan.FromSeconds(2)));
            Subject.Handle(new UploadReopenedEvent(Upload(UploadState.Preparing, sent: 400), "DirectPathLost", Afternoon));

            Rows.Single().Should().Match<TransferStatistic>(s => s.Transfers == 1 && s.Reopened == 1);
        }

        [Test]
        public void a_failed_upload_with_no_reason_should_be_counted_as_failed()
        {
            Subject.Handle(new UploadAttemptEndedEvent(Upload(UploadState.Failed), 0, TimeSpan.Zero));

            Rows.Single().Failure.Should().Be("Failed");
        }

        [Test]
        public void a_title_fetched_twice_in_an_hour_should_be_kept_once()
        {
            Subject.Handle(new UploadAttemptEndedEvent(Upload(UploadState.Completed), 1000, TimeSpan.FromSeconds(1)));
            Subject.Handle(new UploadAttemptEndedEvent(Upload(UploadState.Completed), 1000, TimeSpan.FromSeconds(1)));

            Mocker.Resolve<IFetchedTitleRepository>().All().Should().ContainSingle().Which.Should().Match<FetchedTitle>(t => t.Hour == AfternoonHour && t.ApplicationId == 3 && t.Title == "series-3-12");
        }

        [Test]
        public void requests_and_waits_for_a_free_upload_slot_should_be_counted()
        {
            Subject.Handle(new UploadRequestedEvent(Upload(UploadState.Waiting)));
            Subject.Handle(new UploadRequestedEvent(Upload(UploadState.Waiting)));
            Subject.Handle(new UploadWaitEndedEvent(Upload(UploadState.Sending), TimeSpan.FromSeconds(30)));
            Subject.Handle(new UploadWaitEndedEvent(Upload(UploadState.Sending), TimeSpan.FromSeconds(90)));

            Rows.Sum(s => s.Requests).Should().Be(2);
            Rows.Sum(s => s.Waited).Should().Be(2);
            Rows.Sum(s => s.WaitSeconds).Should().Be(120);
            Rows.Max(s => s.LongestWaitSeconds).Should().Be(90);
        }

        private void Check(ConnectionPath path, DateTime at)
        {
            Subject.Handle(new PeerConnectionCheckedEvent(new Peer { NodeId = "alice", ConnectionPath = path, ConnectionChecked = at }));
        }

        [Test]
        public void connection_checks_should_be_counted_by_what_they_found()
        {
            Check(ConnectionPath.Direct, Afternoon);
            Check(ConnectionPath.Direct, Afternoon.AddMinutes(15));
            Check(ConnectionPath.Relay, Afternoon.AddMinutes(16));
            Check(ConnectionPath.Unknown, Afternoon.AddMinutes(17));

            Mocker.Resolve<IConnectionStatisticRepository>().All().Should().ContainSingle().Which.Should().Match<ConnectionStatistic>(c =>
                c.Hour == AfternoonHour && c.PeerNodeId == "alice" && c.Direct == 2 && c.RelayOnly == 1 && c.None == 1);
        }

        [Test]
        public void a_route_change_should_be_recorded_but_a_friend_going_away_is_not_one()
        {
            Check(ConnectionPath.Direct, Afternoon);
            Mocker.Resolve<IPeerRouteRepository>().FindByNodeId("alice").Should().Match<PeerRoute>(r => r.Path == ConnectionPath.Direct && r.Changed == null);

            Check(ConnectionPath.Unknown, Afternoon.AddMinutes(10));
            Check(ConnectionPath.Direct, Afternoon.AddMinutes(20));
            Mocker.Resolve<IPeerRouteRepository>().FindByNodeId("alice").Changed.Should().BeNull();

            Check(ConnectionPath.Relay, Afternoon.AddHours(1));

            Mocker.Resolve<IPeerRouteRepository>().FindByNodeId("alice").Should().Match<PeerRoute>(r => r.Path == ConnectionPath.Relay && r.Changed == Afternoon.AddHours(1));
            Mocker.Resolve<IConnectionStatisticRepository>().All().Sum(c => c.RouteChanges).Should().Be(1);
            Mocker.Resolve<IConnectionStatisticRepository>().All().Single(c => c.RouteChanges > 0).Hour.Should().Be(AfternoonHour.AddHours(1));
        }
    }
}

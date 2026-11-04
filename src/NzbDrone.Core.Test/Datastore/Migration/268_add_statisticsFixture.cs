using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_statisticsFixture : MigrationTest<add_statistics>
    {
        private static readonly DateTime Ended = DateTime.UtcNow.Date.AddDays(-3).AddHours(14).AddMinutes(37);

        private static TransferJob Job(int id, TransferState state, TransferFailure failure, string category, long bytesDone, string peer = "alice", DateTime? ended = null)
        {
            var end = ended ?? Ended.AddMinutes(id);

            return new TransferJob
            {
                Id = id,
                DownloadId = $"SABnzbd_nzo_{id}",
                CandidateId = $"c-{id}",
                PeerId = 1,
                PeerNodeId = peer,
                RemoteId = $"e-1-{id}",
                Title = $"Pingu.S01E0{id}",
                Category = category,
                Size = 1000,
                BytesDone = bytesDone,
                State = state,
                Failure = failure,
                Added = end.AddHours(-1),
                Started = end.AddMinutes(-4),
                Ended = end
            };
        }

        private static readonly List<TransferJob> Jobs = new()
        {
            Job(1, TransferState.Completed, TransferFailure.None, "tv", 1000),
            Job(2, TransferState.Completed, TransferFailure.None, "TV", 1000),
            Job(3, TransferState.Failed, TransferFailure.NoDirectRoute, "tv", 0),
            Job(4, TransferState.Failed, TransferFailure.DirectPathLost, "movies", 600, peer: "bob"),
            Job(5, TransferState.Completed, TransferFailure.None, "anime", 1000),
            Job(6, TransferState.Completed, TransferFailure.None, "tv", 1000, ended: DateTime.UtcNow.AddMonths(-14))
        };

        private class Row
        {
            public DateTime Hour { get; set; }
            public string PeerNodeId { get; set; }
            public int ApplicationId { get; set; }
            public int Direction { get; set; }
            public string Failure { get; set; }
            public long Bytes { get; set; }
            public int Transfers { get; set; }
            public double Seconds { get; set; }
            public double PeakBytesPerSecond { get; set; }
        }

        [Test]
        public void transfers_already_there_should_make_the_same_statistics_live_counting_would_have()
        {
            using var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Applications").Row(new { Id = 3, Name = "Sonarr", Implementation = "Sonarr", Settings = "{\"baseUrl\": \"http://localhost:8989\", \"downloadCategory\": \"tv\"}", ConfigContract = "SonarrSettings", SyncLevel = 2 });
                m.Insert.IntoTable("Applications").Row(new { Id = 4, Name = "Radarr", Implementation = "Radarr", Settings = "{\"downloadCategory\": \"movies\"}", ConfigContract = "RadarrSettings", SyncLevel = 2 });

                // Still under way: not counted until it ends.
                m.Insert.IntoTable("Transfers").Row(new
                {
                    Id = 7, DownloadId = "SABnzbd_nzo_7", CandidateId = "c-7", PeerId = 1, PeerNodeId = "alice", RemoteId = "e-1-7",
                    MediaKind = 0, Title = "Pingu.S01E07", Category = "tv", Size = 1000, BytesDone = 500, State = (int)TransferState.Downloading, Failure = 0, Attempts = 1, Added = Ended
                });

                foreach (var job in Jobs)
                {
                    m.Insert.IntoTable("Transfers").Row(new
                    {
                        job.Id, job.DownloadId, job.CandidateId, job.PeerId, job.PeerNodeId, job.RemoteId, MediaKind = 0, job.Title, job.Category,
                        job.Size, job.BytesDone, State = (int)job.State, Failure = (int)job.Failure, Attempts = 1, job.Added, job.Started, job.Ended
                    });
                }
            });

            var stored = db.Query<Row>("SELECT * FROM \"TransferStatistics\"").ToList();

            // What StatisticsRecorder adds for each of these as it ends, with the same app for its
            // category.
            var categories = new Dictionary<string, int> { ["tv"] = 3, ["movies"] = 4 };
            var expected = Jobs
                .Where(j => j.Ended > DateTime.UtcNow.AddMonths(-13))
                .Select(j => (Job: j, Live: StatisticsRecorder.ForDownload(j)))
                .GroupBy(x => (x.Live.Hour, x.Job.PeerNodeId, ApplicationId: categories.GetValueOrDefault(x.Job.Category.ToLowerInvariant()), x.Live.Failure))
                .Select(g => new Row
                {
                    Hour = g.Key.Hour,
                    PeerNodeId = g.Key.PeerNodeId,
                    ApplicationId = g.Key.ApplicationId,
                    Direction = (int)TransferDirection.Fetched,
                    Failure = g.Key.Failure,
                    Bytes = g.Sum(x => x.Live.Bytes),
                    Transfers = g.Count(),
                    Seconds = g.Sum(x => x.Live.Seconds),
                    PeakBytesPerSecond = g.Key.Failure.Length == 0 ? g.Max(x => x.Live.Bytes / x.Live.Seconds) : 0
                })
                .ToList();

            // The database keeps 15 significant digits of a double.
            stored.Should().BeEquivalentTo(expected, o => o
                .Using<DateTime>(c => c.Subject.Should().BeCloseTo(c.Expectation, TimeSpan.FromSeconds(1))).WhenTypeIs<DateTime>()
                .Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 1e-9)).WhenTypeIs<double>());
            stored.Should().HaveCount(4);
            stored.Single(r => r.Failure.Length == 0 && r.ApplicationId == 3).Should().Match<Row>(r => r.Transfers == 2 && r.Bytes == 2000 && r.Seconds == 480);
            stored.Single(r => r.Failure.Length == 0 && r.ApplicationId == 0).Transfers.Should().Be(1);
        }

        [Test]
        public void transfers_from_before_should_count_nothing_moved_of_their_own()
        {
            using var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Transfers").Row(new
                {
                    Id = 1, DownloadId = "SABnzbd_nzo_1", CandidateId = "c-1", PeerId = 1, PeerNodeId = "alice", RemoteId = "e-1-1",
                    MediaKind = 0, Title = "Pingu.S01E01", Size = 1000, BytesDone = 500, State = (int)TransferState.Downloading, Failure = 0, Attempts = 1, Added = Ended
                });
            });

            db.QuerySingle<long>("SELECT \"BytesMoved\" FROM \"Transfers\"").Should().Be(0);
            db.QuerySingle<long>("SELECT \"MovingMilliseconds\" FROM \"Transfers\"").Should().Be(0);
            db.Query<Row>("SELECT * FROM \"TransferStatistics\"").Should().BeEmpty();
        }
    }
}

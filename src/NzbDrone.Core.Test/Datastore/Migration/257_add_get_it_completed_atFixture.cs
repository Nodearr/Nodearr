using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_get_it_completed_atFixture : MigrationTest<add_get_it_completed_at>
    {
        private class ReceivedTitle257
        {
            public int GetItState { get; set; }
            public DateTime? GetItCompletedAt { get; set; }
            public DateTime? Handled { get; set; }
        }

        [Test]
        public void older_successes_should_get_a_full_day_from_the_upgrade()
        {
            var before = DateTime.UtcNow;
            var db = WithMigrationTestDb(c =>
            {
                foreach (var state in new[] { GetItState.Done, GetItState.Failed, GetItState.None, GetItState.Working })
                {
                    c.Insert.IntoTable("ReceivedTitles").Row(new
                    {
                        PeerId = 1,
                        SendId = state.ToString(),
                        MediaKind = 0,
                        IsWhole = true,
                        Received = DateTime.UtcNow.AddDays(-7),
                        GetItState = (int)state
                    });
                }
            });

            var received = db.Query<ReceivedTitle257>("SELECT \"GetItState\", \"GetItCompletedAt\", \"Handled\" FROM \"ReceivedTitles\"").ToList();

            received.Single(r => r.GetItState == (int)GetItState.Done).GetItCompletedAt.Should().BeOnOrAfter(before.AddSeconds(-1));
            received.Where(r => r.GetItState != (int)GetItState.Done).Should().OnlyContain(r => r.GetItCompletedAt == null);
            received.Should().OnlyContain(r => r.Handled == null);
        }
    }
}

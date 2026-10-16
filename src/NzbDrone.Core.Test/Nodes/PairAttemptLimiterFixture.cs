using System;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Nodes
{
    [TestFixture]
    public class PairAttemptLimiterFixture : CoreTest<PairAttemptLimiter>
    {
        private static readonly DateTime Start = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        private void GivenFailures(string nodeId, int count, DateTime at)
        {
            for (var i = 0; i < count; i++)
            {
                Subject.Failed(nodeId, at);
            }
        }

        [Test]
        public void a_node_should_be_listened_to_until_it_has_failed_too_often()
        {
            GivenFailures("stranger", PairAttemptLimiter.FailuresPerNode - 1, Start);
            Subject.IsLimited("stranger", Start).Should().BeFalse();

            Subject.Failed("stranger", Start);
            Subject.IsLimited("stranger", Start).Should().BeTrue();
        }

        [Test]
        public void a_limited_node_should_be_listened_to_again_once_its_failures_are_old()
        {
            GivenFailures("stranger", PairAttemptLimiter.FailuresPerNode, Start);

            Subject.IsLimited("stranger", Start + PairAttemptLimiter.Window - TimeSpan.FromSeconds(1)).Should().BeTrue();
            Subject.IsLimited("stranger", Start + PairAttemptLimiter.Window).Should().BeFalse();
        }

        [Test]
        public void one_node_failing_should_not_limit_another()
        {
            GivenFailures("stranger", 50, Start);

            Subject.IsLimited("friend", Start).Should().BeFalse();
        }

        [Test]
        public void a_node_that_pairs_should_start_again_from_nothing()
        {
            GivenFailures("friend", PairAttemptLimiter.FailuresPerNode - 1, Start);
            Subject.Succeeded("friend");
            Subject.Failed("friend", Start);

            Subject.IsLimited("friend", Start).Should().BeFalse();
        }

        [Test]
        public void failures_should_be_logged_one_by_one_until_there_are_many_and_then_counted()
        {
            var logging = Enumerable.Range(0, PairAttemptLimiter.LoggedFailures + 3)
                .Select(i => Subject.Failed($"node-{i}", Start))
                .ToList();

            logging.Take(PairAttemptLimiter.LoggedFailures).Should().OnlyContain(l => l == PairFailureLogging.Log);
            logging[PairAttemptLimiter.LoggedFailures].Should().Be(PairFailureLogging.StopLogging);
            logging.Skip(PairAttemptLimiter.LoggedFailures + 1).Should().OnlyContain(l => l == PairFailureLogging.Count);

            Subject.Failed("later", Start + PairAttemptLimiter.Window).Should().Be(PairFailureLogging.Log);
        }

        [Test]
        public void the_health_check_should_be_told_once_when_a_node_is_first_limited()
        {
            GivenFailures("stranger", PairAttemptLimiter.FailuresPerNode + 3, Start);

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<PairAttemptsLimitedEvent>()), Times.Once());
        }

        [Test]
        public void the_summary_should_count_the_last_hour_of_failures_and_the_nodes_behind_them()
        {
            GivenFailures("old", 3, Start - PairAttemptLimiter.SummaryWindow - TimeSpan.FromMinutes(1));
            GivenFailures("a", PairAttemptLimiter.FailuresPerNode, Start);
            GivenFailures("b", 2, Start);

            var summary = Subject.Summary(Start);

            summary.Failures.Should().Be(PairAttemptLimiter.FailuresPerNode + 2);
            summary.Nodes.Should().Be(2);
            summary.LimitedNodes.Should().Be(1);
        }

        [Test]
        public void many_node_ids_should_not_grow_what_is_kept_without_end()
        {
            for (var i = 0; i < 20000; i++)
            {
                Subject.Failed($"node-{i}", Start);
            }

            Subject.Summary(Start).Nodes.Should().BeLessOrEqualTo(10000);
        }
    }
}

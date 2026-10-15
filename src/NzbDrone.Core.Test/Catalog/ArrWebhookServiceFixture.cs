using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Catalog
{
    [TestFixture]
    public class ArrWebhookServiceFixture : CoreTest<ArrWebhookService>
    {
        private void VerifyQueued(int applicationId, int arrId)
        {
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.Is<CatalogTitleRefreshCommand>(c => c.ApplicationId == applicationId && c.ArrId == arrId), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        private void VerifyNothingQueued()
        {
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<CatalogTitleRefreshCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        // Trimmed from what Sonarr sends.
        [Test]
        public void a_sonarr_import_should_have_its_series_read_again()
        {
            Subject.Receive(1, """{ "eventType": "Download", "series": { "id": 12, "title": "Pingu", "tvdbId": 75669 }, "episodes": [ { "id": 101 } ], "isUpgrade": false }""").Should().BeTrue();

            VerifyQueued(1, 12);
        }

        [Test]
        public void a_radarr_deletion_should_have_its_movie_read_again()
        {
            Subject.Receive(2, """{ "eventType": "MovieFileDelete", "movie": { "id": 20, "title": "Night of the Living Dead" }, "deleteReason": "manual" }""").Should().BeTrue();

            VerifyQueued(2, 20);
        }

        // Sent when the connection is saved. It must be answered, and changes nothing.
        [Test]
        public void a_test_should_be_answered_and_change_nothing()
        {
            Subject.Receive(1, """{ "eventType": "Test", "series": { "id": 1, "title": "Test Title" } }""").Should().BeTrue();

            VerifyNothingQueued();
        }

        [Test]
        public void an_event_about_no_series_or_movie_should_be_answered_and_change_nothing()
        {
            Subject.Receive(1, """{ "eventType": "Health", "level": "warning" }""").Should().BeTrue();

            VerifyNothingQueued();
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("{ \"series\": { \"id\": 12 } }")]
        [TestCase("{ \"eventType\": 5 }")]
        public void a_body_that_is_not_an_arr_webhook_should_be_refused(string body)
        {
            Subject.Receive(1, body).Should().BeFalse();

            VerifyNothingQueued();
        }

        [TestCase("{ \"eventType\": \"Download\", \"series\": { \"id\": -4 } }")]
        [TestCase("{ \"eventType\": \"Download\", \"series\": { \"id\": \"12\" } }")]
        [TestCase("{ \"eventType\": \"Download\", \"series\": 12 }")]
        public void an_id_that_is_not_one_should_change_nothing(string body)
        {
            Subject.Receive(1, body).Should().BeTrue();

            VerifyNothingQueued();
        }
    }
}

using System;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class UpdateCheckFixture : CoreTest<UpdateCheck>
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Warning Message");

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<System.Collections.Generic.Dictionary<string, object>>()))
                  .Returns("Some Notice Message");

            Subject.Now = () => Now;
        }

        private void GivenStatus(UpdateCheckOutcome outcome, DateTime? availableSince = null)
        {
            Mocker.GetMock<ICheckUpdateService>().Setup(c => c.Status()).Returns(new UpdateCheckStatus
            {
                Outcome = outcome,
                Available = availableSince.HasValue ? new UpdatePackage { SemanticVersion = SemanticVersion.Parse("0.10.0") } : null,
                AvailableSince = availableSince
            });
        }

        [Test]
        public void should_warn_when_the_manifest_is_not_signed_by_the_release_key()
        {
            GivenStatus(UpdateCheckOutcome.BadSignature);

            Subject.Check().ShouldBeWarning();
        }

        [Test]
        public void should_say_nothing_when_the_manifest_cannot_be_reached()
        {
            GivenStatus(UpdateCheckOutcome.Unavailable);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_say_nothing_about_a_new_version_for_its_first_14_days()
        {
            GivenStatus(UpdateCheckOutcome.Checked, Now.AddDays(-13));

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_give_notice_of_a_new_version_waiting_14_days()
        {
            GivenStatus(UpdateCheckOutcome.Checked, Now.AddDays(-14));

            Subject.Check().ShouldBeNotice();
        }

        [Test]
        public void should_say_nothing_when_checking_is_off()
        {
            GivenStatus(UpdateCheckOutcome.Off);

            Subject.Check().ShouldBeOk();
        }
    }
}

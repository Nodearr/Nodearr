using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Applications
{
    // A new API key has to reach every Arr app that holds the old one.
    [TestFixture]
    public class ApiKeyResetFixture : CoreTest<ApplicationService>
    {
        private Mock<IApplication> _sonarr;
        private Mock<IApplication> _radarr;

        [SetUp]
        public void Setup()
        {
            _sonarr = GivenApplication(1, "Sonarr");
            _radarr = GivenApplication(2, "Radarr");

            // Every application that is kept in step, including one that has been failing lately.
            Mocker.GetMock<IApplicationFactory>()
                  .Setup(f => f.SyncEnabled(false))
                  .Returns(new List<IApplication> { _sonarr.Object, _radarr.Object });
        }

        private static Mock<IApplication> GivenApplication(int id, string name)
        {
            var application = new Mock<IApplication>();

            application.SetupGet(a => a.Definition).Returns(new ApplicationDefinition { Id = id, Name = name });

            return application;
        }

        [Test]
        public void every_application_should_be_given_the_new_key_at_once()
        {
            Subject.Handle(new ApiKeyResetEvent());

            // Forced: the Arr apps hide the key they hold, so there is nothing to compare it with.
            _sonarr.Verify(a => a.SyncRegistration(true), Times.Once());
            _radarr.Verify(a => a.SyncRegistration(true), Times.Once());
            Mocker.GetMock<IApplicationStatusService>().Verify(s => s.RecordSuccess(1), Times.Once());
        }

        [Test]
        public void one_application_that_cannot_be_reached_should_not_keep_the_key_from_the_rest()
        {
            _sonarr.Setup(a => a.SyncRegistration(true)).Throws(new InvalidOperationException("connection refused"));

            Subject.Handle(new ApiKeyResetEvent());

            _radarr.Verify(a => a.SyncRegistration(true), Times.Once());
            Mocker.GetMock<IApplicationStatusService>().Verify(s => s.RecordFailure(1, It.IsAny<TimeSpan>()), Times.Once());
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}

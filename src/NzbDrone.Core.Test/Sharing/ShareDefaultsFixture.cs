using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Radarr;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Applications.Whisparr;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Test.Sharing
{
    [TestFixture]
    public class ShareDefaultsFixture : DbTest<ShareDefaults, Peer>
    {
        private List<ApplicationDefinition> _applications;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());

            _applications = new List<ApplicationDefinition>
            {
                new() { Id = 1, Name = "Sonarr", Settings = new SonarrSettings() },
                new() { Id = 2, Name = "Radarr", Settings = new RadarrSettings() },
                new() { Id = 3, Name = "Whisparr", Settings = new WhisparrV3Settings() }
            };

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(() => _applications);
        }

        private Peer GivenPeer(string name, params int[] excluded)
        {
            return Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = name, Name = name, State = PeerState.Trusted, ShareAll = true, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow, ExcludedApplicationIds = excluded.ToList() });
        }

        private List<int> ExcludedFor(Peer peer)
        {
            return Mocker.Resolve<IPeerRepository>().Get(peer.Id).ExcludedApplicationIds;
        }

        [Test]
        public void whisparr_should_not_be_shared_with_new_friends_unless_turned_on()
        {
            Subject.NotSharedWithNewFriends().Should().Equal(3);

            ((ArrApplicationSettings)_applications[2].Settings).ShareWithNewFriends = true;
            ((ArrApplicationSettings)_applications[0].Settings).ShareWithNewFriends = false;

            Subject.NotSharedWithNewFriends().Should().Equal(1);
        }

        [Test]
        public void an_app_not_shared_with_new_friends_should_be_left_out_for_the_friends_there_already()
        {
            var bob = GivenPeer("bob");
            var carol = GivenPeer("carol", 2);

            Subject.Handle(new ProviderAddedEvent<IApplication>(new ApplicationDefinition { Id = 4, Settings = new WhisparrV2Settings() }));

            ExcludedFor(bob).Should().Equal(4);
            ExcludedFor(carol).Should().Equal(2, 4);
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<SharingChangedEvent>()), Times.Once());
        }

        [Test]
        public void an_app_shared_with_new_friends_should_change_nothing_when_added()
        {
            var bob = GivenPeer("bob");

            Subject.Handle(new ProviderAddedEvent<IApplication>(new ApplicationDefinition { Id = 4, Settings = new SonarrSettings() }));

            ExcludedFor(bob).Should().BeEmpty();
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<SharingChangedEvent>()), Times.Never());
        }

        [Test]
        public void an_app_removed_should_be_taken_off_every_friend()
        {
            var bob = GivenPeer("bob", 2, 3);
            var carol = GivenPeer("carol", 3);

            Subject.Handle(new ProviderDeletedEvent<IApplication>(3));

            ExcludedFor(bob).Should().Equal(2);
            ExcludedFor(carol).Should().BeEmpty();
        }
    }
}

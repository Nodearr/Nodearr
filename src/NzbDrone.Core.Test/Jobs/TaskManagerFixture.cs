using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Jobs
{
    [TestFixture]
    public class TaskManagerFixture : CoreTest<TaskManager>
    {
        private readonly List<ScheduledTask> _stored = new();

        [SetUp]
        public void Setup()
        {
            _stored.Clear();

            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BackupInterval).Returns(7);

            var repository = Mocker.GetMock<IScheduledTaskRepository>();

            repository.Setup(r => r.All()).Returns(() => _stored.ToList());
            repository.Setup(r => r.Upsert(It.IsAny<ScheduledTask>())).Callback<ScheduledTask>(t =>
            {
                _stored.RemoveAll(s => s.TypeName == t.TypeName);
                _stored.Add(t);
            });
            repository.Setup(r => r.GetDefinition(It.IsAny<System.Type>())).Returns<System.Type>(type => _stored.Single(s => s.TypeName == type.FullName));
        }

        private void GivenCatalogSyncInterval(int minutes)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.CatalogSyncInterval).Returns(minutes);
        }

        private int IntervalOf<TCommand>()
        {
            return Subject.GetAll().Single(t => t.TypeName == typeof(TCommand).FullName).Interval;
        }

        // As Sonarr treats its RSS Sync Interval.
        [TestCase(15, 15)]
        [TestCase(0, 0)]
        [TestCase(5, 10)]
        [TestCase(500, 120)]
        [TestCase(-3, 0)]
        public void peer_catalogs_should_be_synced_at_the_interval_set(int setting, int expected)
        {
            GivenCatalogSyncInterval(setting);

            Subject.Handle(new ApplicationStartedEvent());

            IntervalOf<PeerCatalogSyncCommand>().Should().Be(expected);
        }

        [Test]
        public void an_interval_of_0_should_never_be_due()
        {
            GivenCatalogSyncInterval(0);

            Subject.Handle(new ApplicationStartedEvent());

            Subject.GetPending().Should().NotContain(t => t.TypeName == typeof(PeerCatalogSyncCommand).FullName);
        }

        [Test]
        public void a_new_interval_should_take_effect_when_the_configuration_is_saved()
        {
            GivenCatalogSyncInterval(15);
            Subject.Handle(new ApplicationStartedEvent());

            GivenCatalogSyncInterval(45);
            Subject.HandleAsync(new ConfigSavedEvent());

            IntervalOf<PeerCatalogSyncCommand>().Should().Be(45);
        }

        // As often as Sonarr refreshes its series.
        [Test]
        public void the_whole_library_should_be_read_from_the_arr_apps_every_12_hours()
        {
            GivenCatalogSyncInterval(15);

            Subject.Handle(new ApplicationStartedEvent());

            IntervalOf<CatalogRefreshCommand>().Should().Be(12 * 60);
        }
    }
}

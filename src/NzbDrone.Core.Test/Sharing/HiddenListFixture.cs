using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Sharing
{
    // Two requests that look for the Hidden list at the same moment must not each make one.
    [TestFixture]
    public class HiddenListFixture : CoreTest<SharingService>
    {
        private readonly List<ShareList> _lists = new();
        private readonly object _store = new();
        private Barrier _bothLooking;

        [SetUp]
        public void Setup()
        {
            // The first two to read the lists wait for each other, for half a second at most, so
            // both see what was there before either could add to it.
            _bothLooking = new Barrier(2);
            var readers = 0;

            var lists = Mocker.GetMock<IShareListRepository>();

            lists.Setup(r => r.All()).Returns(() =>
            {
                if (Interlocked.Increment(ref readers) <= 2)
                {
                    _bothLooking.SignalAndWait(TimeSpan.FromMilliseconds(500));
                }

                lock (_store)
                {
                    return _lists.ToList();
                }
            });

            lists.Setup(r => r.Insert(It.IsAny<ShareList>())).Returns<ShareList>(l =>
            {
                lock (_store)
                {
                    l.Id = _lists.Count + 1;
                    _lists.Add(l);
                }

                return l;
            });

            Mocker.GetMock<IShareListItemRepository>().Setup(r => r.All()).Returns(new List<ShareListItem>());
            Mocker.GetMock<IShareListPeerRepository>().Setup(r => r.All()).Returns(new List<ShareListPeer>());
        }

        [Test]
        public void two_requests_at_once_should_make_one_hidden_list()
        {
            Task.WaitAll(Task.Run(() => Subject.All()), Task.Run(() => Subject.All()));

            _lists.Count(l => l.IsHidden).Should().Be(1);
        }
    }
}

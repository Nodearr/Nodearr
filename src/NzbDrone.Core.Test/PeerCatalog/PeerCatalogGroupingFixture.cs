using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.PeerCatalog
{
    [TestFixture]
    public class PeerCatalogGroupingFixture : TestBase
    {
        [Test]
        public void a_missing_copy_takes_priority_over_upgrade_and_have()
        {
            var items = new[]
            {
                new PeerCatalogItem { LocalStatus = PeerItemStatus.Have },
                new PeerCatalogItem { LocalStatus = PeerItemStatus.Upgrade },
                new PeerCatalogItem { LocalStatus = PeerItemStatus.Missing }
            };

            PeerCatalogGrouping.BestStatus(items).Should().Be(PeerItemStatus.Missing);
        }

        [Test]
        public void episode_summaries_count_each_tvdb_episode_once_with_its_best_peer_status()
        {
            var items = new List<PeerCatalogItem>
            {
                new() { EpisodeNumbers = "4,5", LocalStatus = PeerItemStatus.Missing },
                new() { EpisodeNumbers = "4", LocalStatus = PeerItemStatus.Upgrade },
                new() { EpisodeNumbers = "5", LocalStatus = PeerItemStatus.Have }
            };

            var statuses = PeerCatalogGrouping.EpisodeStatuses(items);

            statuses.Should().HaveCount(2);
            statuses[4].Should().Be(PeerItemStatus.Missing);
            statuses[5].Should().Be(PeerItemStatus.Missing);
        }
    }
}

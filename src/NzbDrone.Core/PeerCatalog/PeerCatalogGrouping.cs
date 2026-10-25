using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.PeerCatalog
{
    public static class PeerCatalogGrouping
    {
        // Any missing episode makes its show/season missing. Otherwise an upgrade takes priority
        // over files the local library already has at equal or better quality.
        public static PeerItemStatus BestStatus(IEnumerable<PeerCatalogItem> items)
        {
            return items.Select(i => i.LocalStatus).OrderBy(Priority).FirstOrDefault();
        }

        // Peers can offer the same episode more than once. Count it once, with the best status any
        // peer offers, so the show and season summaries describe the episodes rather than copies.
        public static Dictionary<int, PeerItemStatus> EpisodeStatuses(IEnumerable<PeerCatalogItem> items)
        {
            return items.SelectMany(item => item.GetEpisodeNumbers().Select(number => (Number: number, item.LocalStatus)))
                .GroupBy(episode => episode.Number)
                .ToDictionary(group => group.Key, group => group.Select(episode => episode.LocalStatus).OrderBy(Priority).First());
        }

        private static int Priority(PeerItemStatus status)
        {
            return status switch
            {
                PeerItemStatus.Missing => 0,
                PeerItemStatus.Upgrade => 1,
                PeerItemStatus.NotInLibrary => 2,
                _ => 3
            };
        }
    }
}

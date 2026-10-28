using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace Nodearr.Api.V5.Catalog;

// Catalog navigation and received sends read the same cached details and actions.
public static class PeerCatalogDetails
{
    public static CatalogDetailsResource? Get(IPeerCatalogService peerCatalog, ILocalHoldingsFactory localHoldings, string kind, int itemId, int? episodeIndex = null)
    {
        var item = peerCatalog.GetItem(itemId);
        if (item == null || item.MediaKind == MediaKind.Music)
        {
            return null;
        }

        var summaries = peerCatalog.GetSummaries();
        var trusted = summaries.Select(s => s.PeerId).ToHashSet();

        if (kind == "episode")
        {
            var numbers = item.GetEpisodeNumbers();
            if (item.MediaKind != MediaKind.Tv || episodeIndex is not >= 0 || episodeIndex >= numbers.Count)
            {
                return null;
            }

            var details = item.GetEpisodeDetails();
            var selected = episodeIndex.Value < details.Count ? details[episodeIndex.Value] : null;
            var absoluteNumbers = item.GetAbsoluteEpisodeNumbers();
            var airDates = item.GetAirDates();
            var localStatus = localHoldings.Load().ClassifyEpisode(item, numbers[episodeIndex.Value]);
            return new CatalogDetailsResource
            {
                Kind = kind,
                Title = selected?.Title ?? (numbers.Count == 1 ? item.EpisodeTitles : null),
                ParentTitle = item.Title,
                SourceName = summaries.FirstOrDefault(s => s.PeerId == item.PeerId)?.PeerName,
                PosterPath = $"/catalog/posters/peer/{item.Id}",
                Year = item.Year,
                Overview = selected?.Overview,
                Genres = item.Genres,
                Network = item.Network,
                Runtime = item.Runtime,
                SeasonNumber = item.SeasonNumber,
                EpisodeNumber = numbers[episodeIndex.Value],
                AbsoluteEpisodeNumber = absoluteNumbers.Count == numbers.Count ? absoluteNumbers[episodeIndex.Value] : null,
                AirDate = airDates.Count == numbers.Count ? airDates[episodeIndex.Value] : null,
                TvdbId = item.TvdbId,
                TmdbId = item.TmdbId,
                ImdbId = item.ImdbId,
                Quality = item.Quality,
                Resolution = item.Resolution,
                Size = item.Size,
                LocalStatus = localStatus,
                GetIt = trusted.Contains(item.PeerId) && localStatus != PeerItemStatus.Have
                    ? new CatalogDetailsGetItResource { ItemId = item.Id, Scope = CatalogGetItScope.File, MediaKind = item.MediaKind }
                    : null
            };
        }

        var actualKind = item.MediaKind == MediaKind.Tv ? "series" :
            item.IsScene ? "scene" : item.IsAdult ? "film" : "movie";
        if (kind != actualKind)
        {
            return null;
        }

        var seriesItems = kind == "series"
            ? peerCatalog.GetSeries(null)?.FirstOrDefault(s => s.Key == NzbDrone.Core.Posters.PeerPosters.KeyOf(item))?.Items ?? new List<PeerCatalogItem> { item }
            : new List<PeerCatalogItem>();
        var holdings = localHoldings.Load();
        var getItem = (kind == "series" ? seriesItems : new List<PeerCatalogItem> { item })
            .FirstOrDefault(i => trusted.Contains(i.PeerId) && holdings.Classify(i) != PeerItemStatus.Have);

        return new CatalogDetailsResource
        {
            Kind = kind,
            Title = item.Title,
            SourceName = summaries.FirstOrDefault(s => s.PeerId == item.PeerId)?.PeerName,
            PosterPath = $"/catalog/posters/peer/{item.Id}",
            Year = item.Year,
            Overview = item.Overview,
            Genres = item.Genres,
            Network = item.Network,
            Runtime = item.Runtime,
            Studio = item.Studio,
            ReleaseDate = item.ReleaseDate,
            Code = item.Code,
            TvdbId = item.TvdbId,
            TmdbId = item.TmdbId,
            ImdbId = item.ImdbId,
            Quality = kind == "series" ? null : item.Quality,
            LocalStatus = kind == "series" ? null : holdings.Classify(item),
            Resolution = kind == "series" ? 0 : item.Resolution,
            Resolutions = seriesItems.Where(i => i.Resolution > 0).Select(i => i.Resolution).Distinct().OrderByDescending(r => r).ToList(),
            Episodes = seriesItems.Where(i => i.PeerId == item.PeerId)
                .SelectMany(i => i.GetEpisodeNumbers().Select((number, index) => new CatalogDetailsEpisodeResource
                {
                    SeasonNumber = i.SeasonNumber,
                    EpisodeNumber = number,
                    HasFile = true,
                    LocalStatus = holdings.ClassifyEpisode(i, number),
                    Title = i.GetEpisodeDetails().ElementAtOrDefault(index)?.Title ?? (i.GetEpisodeNumbers().Count == 1 ? i.EpisodeTitles : null),
                    Path = $"/catalog/peers/details/episode/{i.Id}?episodeIndex={index}"
                }))
                .GroupBy(e => (e.SeasonNumber, e.EpisodeNumber))
                .Select(g => g.First())
                .OrderBy(e => e.SeasonNumber == 0 ? int.MaxValue : e.SeasonNumber)
                .ThenBy(e => e.EpisodeNumber).ToList(),
            Size = kind == "series" ? 0 : item.Size,
            GetIt = getItem == null ? null : new CatalogDetailsGetItResource
            {
                // Keep the series anchor stable as individual episodes finish importing, so an
                // open Get it dialog can still find the command that started from this card.
                ItemId = trusted.Contains(item.PeerId) ? item.Id : getItem.Id,
                Scope = kind == "series" ? CatalogGetItScope.Title : CatalogGetItScope.File,
                MediaKind = item.MediaKind
            }
        };
    }
}

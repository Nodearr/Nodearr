using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Posters;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.PeerCatalog
{
    public enum CatalogGetItScope
    {
        File,
        Season,
        Title
    }

    public class CatalogGetItPlan
    {
        public GetItPlan Plan { get; set; }
        public List<int> Seasons { get; set; } = new();
        public List<PeerCatalogItem> Files { get; set; } = new();
    }

    public class CatalogGetItCommand : Command
    {
        public int ItemId { get; set; }
        public CatalogGetItScope Scope { get; set; }
        public int? SeasonNumber { get; set; }
        public GetItOptions Options { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool UpdateScheduledTask => false;
    }

    public interface IPeerCatalogGetItService
    {
        CatalogGetItPlan Plan(int itemId, CatalogGetItScope scope, int? seasonNumber);
        CommandModel GetIt(int itemId, CatalogGetItScope scope, int? seasonNumber, GetItOptions options);
    }

    public class PeerCatalogGetItService : IPeerCatalogGetItService, IExecute<CatalogGetItCommand>
    {
        private readonly IPeerCatalogRepository _items;
        private readonly IPeerRepository _peers;
        private readonly ILocalHoldingsFactory _holdings;
        private readonly ILibraryGetItService _library;
        private readonly IManageCommandQueue _commands;
        private readonly Logger _logger;

        public PeerCatalogGetItService(IPeerCatalogRepository items,
                                       IPeerRepository peers,
                                       ILocalHoldingsFactory holdings,
                                       ILibraryGetItService library,
                                       IManageCommandQueue commands,
                                       Logger logger)
        {
            _items = items;
            _peers = peers;
            _holdings = holdings;
            _library = library;
            _commands = commands;
            _logger = logger;
        }

        public CatalogGetItPlan Plan(int itemId, CatalogGetItScope scope, int? seasonNumber)
        {
            var (title, have) = TitleFor(itemId, scope, seasonNumber, allowHave: true);

            return new CatalogGetItPlan
            {
                Plan = have ? new GetItPlan { Problem = "You already have this selection. Choose something missing or an upgrade" } : _library.Plan(title),
                Seasons = title.MediaKind == MediaKind.Tv ? title.Offers.Select(i => i.SeasonNumber).Distinct().OrderBy(n => n).ToList() : new(),
                Files = title.Offers
            };
        }

        public CommandModel GetIt(int itemId, CatalogGetItScope scope, int? seasonNumber, GetItOptions options)
        {
            var (title, _) = TitleFor(itemId, scope, seasonNumber);
            _library.CheckOptions(title, options);

            return _commands.Push(
                new CatalogGetItCommand
                {
                    ItemId = itemId,
                    Scope = scope,
                    SeasonNumber = seasonNumber,
                    Options = options
                },
                CommandPriority.Normal,
                CommandTrigger.Manual);
        }

        public void Execute(CatalogGetItCommand message)
        {
            try
            {
                // Trust, availability and holdings may have changed while the command waited.
                var (title, _) = TitleFor(message.ItemId, message.Scope, message.SeasonNumber);
                var done = _library.AddAndSearch(title, message.Options);

                _logger.ProgressInfo("{0}: {1}", title.Title, done);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to get catalog item {0}", message.ItemId);
                var content = ex is HttpException http ? (http.Response.Content ?? string.Empty).Trim() : null;
                var reason = ex is HttpException answer ? $"The app answered {(int)answer.Response.StatusCode}: {(content.Length > 300 ? content[..300] : content)}" : ex.Message;

                throw new CommandFailedException(reason, ex);
            }
        }

        private (GetItTitle Title, bool Have) TitleFor(int itemId, CatalogGetItScope scope, int? seasonNumber, bool allowHave = false)
        {
            var item = _items.Find(itemId) ?? throw new GetItException("This file is no longer offered. Sync your friends' catalogs, then choose again");
            var trusted = _peers.All().Where(p => p.State == PeerState.Trusted).Select(p => p.Id).ToHashSet();

            if (!trusted.Contains(item.PeerId))
            {
                throw new GetItException("This friend is no longer trusted. Choose a file from a trusted friend");
            }

            if (!Enum.IsDefined(scope) || (scope == CatalogGetItScope.Season && (item.MediaKind != MediaKind.Tv || !seasonNumber.HasValue)))
            {
                throw new GetItException("Choose a file, a season or a whole title to get");
            }

            if (item.MediaKind == MediaKind.Music && !item.IsAlbum)
            {
                throw new GetItException("This album has no usable tracks. Sync your friend's catalog, then try again");
            }

            var key = PeerPosters.KeyOf(item);
            var offers = _items.All().Where(i => trusted.Contains(i.PeerId) && PeerPosters.KeyOf(i) == key).ToList();
            var selected = offers.Where(i => scope switch
            {
                CatalogGetItScope.File => i.Id == item.Id,
                CatalogGetItScope.Season => i.SeasonNumber == seasonNumber,
                _ => true
            }).ToList();

            if (selected.Count == 0)
            {
                throw new GetItException("Your friends no longer offer that selection. Sync their catalogs, then choose again");
            }

            var holdings = _holdings.Load();

            foreach (var offer in offers)
            {
                offer.LocalStatus = holdings.Classify(offer);
            }

            var have = selected.All(i => i.LocalStatus == PeerItemStatus.Have);

            if (have && !allowHave)
            {
                throw new GetItException("You already have this selection. Choose something missing or an upgrade");
            }

            var parts = new SentParts();

            if (item.MediaKind == MediaKind.Tv && scope == CatalogGetItScope.Season)
            {
                parts.Seasons.Add(seasonNumber.Value);
            }
            else if (item.MediaKind == MediaKind.Tv && scope == CatalogGetItScope.File)
            {
                parts.Episodes = selected.SelectMany(i => i.GetEpisodeNumbers().Select(e => new SeasonEpisode { SeasonNumber = i.SeasonNumber, EpisodeNumber = e })).ToList();
            }
            else if (item.MediaKind == MediaKind.Music)
            {
                parts.Albums = selected.Select(i => i.ForeignAlbumId).Where(id => id != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            return (new GetItTitle
            {
                Title = item.MediaKind == MediaKind.Music ? $"{item.Artist} - {item.Title}" : item.Title,
                MediaKind = item.MediaKind,
                IsAdult = item.IsAdult,
                IsWhole = item.MediaKind == MediaKind.Tv && scope == CatalogGetItScope.Title,
                TvdbId = item.TvdbId,
                TmdbId = item.TmdbId,
                ImdbId = item.ImdbId,
                ForeignArtistId = item.ForeignArtistId,
                ForeignId = item.ForeignId,
                StashId = item.StashId,
                TpdbId = item.TpdbId,
                SeriesType = item.SeriesType,
                Parts = parts,
                OfferIds = string.Join(",", selected.Select(i => i.RemoteId)),
                Offers = offers
            }, have);
        }
    }
}

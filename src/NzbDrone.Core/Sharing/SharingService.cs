using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Sharing
{
    public interface ISharingService
    {
        List<ShareListDetails> All();
        ShareListDetails Create(string name);
        ShareListDetails Update(int id, string name, IEnumerable<ShareKey> items, IEnumerable<int> peerIds);
        void Delete(int id);
        List<ShareableTitle> GetTitles();

        // The seasons and episodes of one series, for choosing among them.
        List<ShareableSeason> GetSeasons(int applicationId, int seriesArrId);
        Peer SetShareAll(int peerId, bool shareAll);
        Offer BuildOffer(Peer peer);

        // Whether a peer is offered a file right now, by the name its offer gives the file.
        bool IsOffered(Peer peer, string offerFileId);
    }

    // Decides what each peer is told about. The rule, in order:
    //   1. Nothing is offered to a peer that is not trusted.
    //   2. Anything on the Hidden list is offered to nobody.
    //   3. A peer marked "share everything" is offered the rest.
    //   4. Otherwise a peer is offered what is on the share lists it has been given.
    // A list holds whole series, seasons, single episodes and movies, the way Sonarr monitors. An
    // episode is covered by its series, its season, or itself. A file holding several episodes is
    // offered only when every one of them is, as Sonarr wants a release only when every episode
    // in it is monitored.
    // The filtering happens here, before anything is sent. A peer never receives an item and a
    // flag saying not to show it.
    public class SharingService : ISharingService,
                                  IHandle<ProviderDeletedEvent<IApplication>>,
                                  IHandle<PeerDeletedEvent>
    {
        public const string HiddenListName = "Hidden";

        private readonly IShareListRepository _lists;
        private readonly IShareListItemRepository _items;
        private readonly IShareListPeerRepository _listPeers;
        private readonly IPeerRepository _peers;
        private readonly IApplicationFactory _applicationFactory;
        private readonly ICatalogSeriesRepository _series;
        private readonly ICatalogEpisodeRepository _episodes;
        private readonly ICatalogEpisodeFileRepository _episodeFiles;
        private readonly ICatalogMovieRepository _movies;
        private readonly ICatalogMovieFileRepository _movieFiles;
        private readonly IFileHashRepository _fileHashes;
        private readonly IEventAggregator _eventAggregator;

        public SharingService(IShareListRepository lists,
                              IShareListItemRepository items,
                              IShareListPeerRepository listPeers,
                              IPeerRepository peers,
                              IApplicationFactory applicationFactory,
                              ICatalogSeriesRepository series,
                              ICatalogEpisodeRepository episodes,
                              ICatalogEpisodeFileRepository episodeFiles,
                              ICatalogMovieRepository movies,
                              ICatalogMovieFileRepository movieFiles,
                              IFileHashRepository fileHashes,
                              IEventAggregator eventAggregator)
        {
            _fileHashes = fileHashes;
            _eventAggregator = eventAggregator;
            _lists = lists;
            _items = items;
            _listPeers = listPeers;
            _peers = peers;
            _applicationFactory = applicationFactory;
            _series = series;
            _episodes = episodes;
            _episodeFiles = episodeFiles;
            _movies = movies;
            _movieFiles = movieFiles;
        }

        public List<ShareListDetails> All()
        {
            EnsureHiddenList();

            var items = _items.All().ToLookup(i => i.ShareListId);
            var peers = _listPeers.All().ToLookup(p => p.ShareListId);

            return _lists.All()
                .OrderByDescending(l => l.IsHidden)
                .ThenBy(l => l.Name)
                .Select(l => new ShareListDetails
                {
                    List = l,
                    Items = items[l.Id].Select(i => i.Key).ToList(),
                    PeerIds = peers[l.Id].Select(p => p.PeerId).ToList()
                })
                .ToList();
        }

        public ShareListDetails Create(string name)
        {
            if (name.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("A share list needs a name", nameof(name));
            }

            return new ShareListDetails { List = _lists.Insert(new ShareList { Name = name.Trim() }) };
        }

        public ShareListDetails Update(int id, string name, IEnumerable<ShareKey> items, IEnumerable<int> peerIds)
        {
            var list = _lists.Get(id);

            // The Hidden list keeps its name and is never given to anyone.
            if (!list.IsHidden)
            {
                if (name.IsNotNullOrWhiteSpace())
                {
                    list.Name = name.Trim();
                    _lists.Update(list);
                }

                _listPeers.DeleteForList(id);
                _listPeers.InsertMany((peerIds ?? Enumerable.Empty<int>()).Distinct().Select(p => new ShareListPeer { ShareListId = id, PeerId = p }).ToList());
            }

            _items.DeleteForList(id);
            _items.InsertMany(Tidy(items).Select(k => new ShareListItem
            {
                ShareListId = id,
                MediaKind = k.Kind,
                ApplicationId = k.ApplicationId,
                ArrId = k.ArrId,
                SeasonNumber = k.SeasonNumber,
                EpisodeArrId = k.EpisodeArrId
            }).ToList());

            _eventAggregator.PublishEvent(new SharingChangedEvent());

            return All().Single(l => l.List.Id == id);
        }

        public void Delete(int id)
        {
            if (_lists.Get(id).IsHidden)
            {
                throw new InvalidOperationException("The Hidden list cannot be deleted");
            }

            _items.DeleteForList(id);
            _listPeers.DeleteForList(id);
            _lists.Delete(id);
            _eventAggregator.PublishEvent(new SharingChangedEvent());
        }

        public List<ShareableTitle> GetTitles()
        {
            var applications = _applicationFactory.All().ToDictionary(a => a.Id, a => a.Name);
            var episodeFiles = _episodeFiles.All().Where(f => f.MissingSince == null).ToLookup(f => (f.ApplicationId, f.ArrSeriesId));
            var movieFiles = _movieFiles.All().Where(f => f.MissingSince == null).ToLookup(f => (f.ApplicationId, f.ArrMovieId));

            var series = _series.All().Where(s => s.MissingSince == null).Select(s => new ShareableTitle
            {
                Key = new TitleKey(MediaKind.Tv, s.ApplicationId, s.ArrId),
                ApplicationName = applications.GetValueOrDefault(s.ApplicationId),
                Title = s.Title,
                Year = s.Year,
                Files = episodeFiles[(s.ApplicationId, s.ArrId)].Count()
            });

            var movies = _movies.All().Where(m => m.MissingSince == null).Select(m => new ShareableTitle
            {
                Key = new TitleKey(MediaKind.Movie, m.ApplicationId, m.ArrId),
                ApplicationName = applications.GetValueOrDefault(m.ApplicationId),
                Title = m.Title,
                Year = m.Year,
                Files = movieFiles[(m.ApplicationId, m.ArrId)].Count()
            });

            return series.Concat(movies).OrderBy(t => t.Title).ToList();
        }

        public List<ShareableSeason> GetSeasons(int applicationId, int seriesArrId)
        {
            return _episodes.ForSeries(applicationId, seriesArrId)
                .Where(e => e.MissingSince == null)
                .GroupBy(e => e.SeasonNumber)
                .OrderBy(g => g.Key)
                .Select(g => new ShareableSeason
                {
                    SeasonNumber = g.Key,
                    Episodes = g.OrderBy(e => e.EpisodeNumber).Select(e => new ShareableEpisode
                    {
                        EpisodeArrId = e.ArrId,
                        EpisodeNumber = e.EpisodeNumber,
                        Title = e.Title,
                        HasFile = e.ArrEpisodeFileId > 0
                    }).ToList()
                })
                .ToList();
        }

        public Peer SetShareAll(int peerId, bool shareAll)
        {
            var peer = _peers.Get(peerId);

            peer.ShareAll = shareAll;
            _peers.SetFields(peer, p => p.ShareAll);
            _eventAggregator.PublishEvent(new SharingChangedEvent());

            return peer;
        }

        public Offer BuildOffer(Peer peer)
        {
            if (peer is not { State: PeerState.Trusted })
            {
                return Seal(new List<OfferItem>());
            }

            var rule = RuleFor(peer);
            var offered = new List<OfferItem>();

            if (rule.SharesAnyTv)
            {
                var series = _series.All()
                    .Where(s => s.MissingSince == null)
                    .ToDictionary(s => (s.ApplicationId, s.ArrId));

                var episodes = _episodes.All()
                    .Where(e => e.MissingSince == null && e.ArrEpisodeFileId > 0)
                    .ToLookup(e => (e.ApplicationId, e.ArrEpisodeFileId));

                foreach (var file in _episodeFiles.All().Where(f => f.MissingSince == null))
                {
                    if (!series.TryGetValue((file.ApplicationId, file.ArrSeriesId), out var parent))
                    {
                        continue;
                    }

                    var contained = episodes[(file.ApplicationId, file.ArrId)].OrderBy(e => e.EpisodeNumber).ToList();

                    // A file Sonarr has not tied to an episode says nothing a peer could search for.
                    if (!rule.SharesFile(contained))
                    {
                        continue;
                    }

                    var item = Describe(file, MediaKind.Tv, $"e-{file.ApplicationId}-{file.ArrId}");

                    item.TvdbId = parent.TvdbId;
                    item.TmdbId = parent.TmdbId;
                    item.ImdbId = parent.ImdbId;
                    item.Title = parent.Title;
                    item.Year = parent.Year;
                    item.SeasonNumber = file.SeasonNumber;
                    item.EpisodeNumbers = contained.Select(e => e.EpisodeNumber).ToList();
                    item.EpisodeTitles = contained.Select(e => e.Title ?? string.Empty).ToList();
                    item.AirDates = contained.Select(e => e.AirDate).Where(d => d.IsNotNullOrWhiteSpace()).ToList();
                    (item.SceneSeasonNumber, item.SceneEpisodeNumbers) = SceneNumbers(contained);
                    offered.Add(item);
                }
            }

            var movies = _movies.All()
                .Where(m => m.MissingSince == null && rule.SharesMovie(m.ApplicationId, m.ArrId))
                .ToDictionary(m => (m.ApplicationId, m.ArrId));

            if (movies.Count > 0)
            {
                foreach (var file in _movieFiles.All().Where(f => f.MissingSince == null))
                {
                    if (!movies.TryGetValue((file.ApplicationId, file.ArrMovieId), out var parent))
                    {
                        continue;
                    }

                    var item = Describe(file, MediaKind.Movie, $"m-{file.ApplicationId}-{file.ArrId}");

                    item.TmdbId = parent.TmdbId;
                    item.ImdbId = parent.ImdbId;
                    item.Title = parent.Title;
                    item.Year = parent.Year;

                    if (parent.IsAdult)
                    {
                        item.Adult = true;
                        item.ItemType = parent.ItemType;
                        item.Studio = parent.Studio;
                        item.ReleaseDate = parent.ReleaseDate;
                        item.Code = parent.Code;
                        item.ForeignId = parent.ForeignId;
                        item.StashId = parent.StashId;
                        item.TpdbId = parent.TpdbId;
                    }

                    offered.Add(item);
                }
            }

            // The hash, where it has been worked out for a file the size the Arr app says it is.
            if (offered.Count > 0)
            {
                var hashes = _fileHashes.ByOfferFileId();

                foreach (var item in offered)
                {
                    if (hashes.TryGetValue(item.Id, out var hash) && hash.Size == item.Size)
                    {
                        item.Blake3 = hash.Blake3;
                    }
                }
            }

            return Seal(offered);
        }

        // The same answer as looking for the file in BuildOffer(peer), from the file's own rows. It
        // is asked for every file request, and building the whole offer for a large library takes
        // most of a second.
        public bool IsOffered(Peer peer, string offerFileId)
        {
            if (peer is not { State: PeerState.Trusted } || !TryParseFileId(offerFileId, out var kind, out var applicationId, out var arrFileId))
            {
                return false;
            }

            if (kind == MediaKind.Tv)
            {
                var file = _episodeFiles.ForArrIds(applicationId, new[] { arrFileId }).FirstOrDefault(f => f.MissingSince == null);
                var series = file == null ? null : _series.ForArrIds(applicationId, new[] { file.ArrSeriesId }).FirstOrDefault(s => s.MissingSince == null);

                return series != null &&
                       RuleFor(peer).SharesFile(_episodes.ForFiles(applicationId, new[] { arrFileId }).Where(e => e.MissingSince == null).ToList());
            }

            var movieFile = _movieFiles.ForArrIds(applicationId, new[] { arrFileId }).FirstOrDefault(f => f.MissingSince == null);
            var movie = movieFile == null ? null : _movies.ForArrIds(applicationId, new[] { movieFile.ArrMovieId }).FirstOrDefault(m => m.MissingSince == null);

            return movie != null && RuleFor(peer).SharesMovie(applicationId, movie.ArrId);
        }

        // Rules 2 to 4, for one peer that is already known to be trusted.
        private SharingRule RuleFor(Peer peer)
        {
            var lists = All();
            var hidden = lists.Where(l => l.List.IsHidden).SelectMany(l => l.Items).ToHashSet();
            var listed = lists.Where(l => !l.List.IsHidden && l.PeerIds.Contains(peer.Id)).SelectMany(l => l.Items).ToHashSet();

            return new SharingRule(hidden, listed, peer.ShareAll);
        }

        // What a list may hold: a movie whole, and a series whole, by season or by episode. A
        // narrower item under a whole series on the same list says nothing more, so it is dropped.
        private static List<ShareKey> Tidy(IEnumerable<ShareKey> items)
        {
            var keys = (items ?? Enumerable.Empty<ShareKey>())
                .Select(k => k.Kind == MediaKind.Movie ? new ShareKey(MediaKind.Movie, k.ApplicationId, k.ArrId) : k)
                .Where(k => k.SeasonNumber is null or >= 0 && !(k.SeasonNumber.HasValue && k.EpisodeArrId.HasValue))
                .Distinct()
                .ToList();

            var whole = keys.Where(k => k.IsWhole).Select(k => k.Title).ToHashSet();

            return keys.Where(k => k.IsWhole || !whole.Contains(k.Title)).ToList();
        }

        private sealed class SharingRule
        {
            private readonly HashSet<ShareKey> _hidden;
            private readonly HashSet<ShareKey> _listed;
            private readonly bool _shareAll;

            public SharingRule(HashSet<ShareKey> hidden, HashSet<ShareKey> listed, bool shareAll)
            {
                _hidden = hidden;
                _listed = listed;
                _shareAll = shareAll;
            }

            public bool SharesAnyTv => _shareAll || _listed.Any(k => k.Kind == MediaKind.Tv);

            public bool SharesMovie(int applicationId, int movieArrId)
            {
                var key = ShareKey.Movie(applicationId, movieArrId);

                return !_hidden.Contains(key) && (_shareAll || _listed.Contains(key));
            }

            public bool SharesFile(List<CatalogEpisode> contained)
            {
                return contained.Count > 0 && contained.All(SharesEpisode);
            }

            private bool SharesEpisode(CatalogEpisode episode)
            {
                return !Covers(_hidden, episode) && (_shareAll || Covers(_listed, episode));
            }

            private static bool Covers(HashSet<ShareKey> keys, CatalogEpisode episode)
            {
                return keys.Contains(ShareKey.Series(episode.ApplicationId, episode.ArrSeriesId)) ||
                       keys.Contains(ShareKey.Season(episode.ApplicationId, episode.ArrSeriesId, episode.SeasonNumber)) ||
                       keys.Contains(ShareKey.Episode(episode.ApplicationId, episode.ArrSeriesId, episode.ArrId));
            }
        }

        // An offer names a file e-{application}-{file} or m-{application}-{file}, and nothing else
        // names the same file.
        private static bool TryParseFileId(string offerFileId, out MediaKind kind, out int applicationId, out int arrFileId)
        {
            kind = MediaKind.Tv;
            applicationId = 0;
            arrFileId = 0;

            var parts = (offerFileId ?? string.Empty).Split('-');

            if (parts.Length != 3 || parts[0] is not ("e" or "m") || !int.TryParse(parts[1], out applicationId) || !int.TryParse(parts[2], out arrFileId))
            {
                return false;
            }

            kind = parts[0] == "e" ? MediaKind.Tv : MediaKind.Movie;

            return offerFileId == $"{parts[0]}-{applicationId}-{arrFileId}";
        }

        public void Handle(ProviderDeletedEvent<IApplication> message)
        {
            _items.DeleteForApplication(message.ProviderId);
        }

        public void Handle(PeerDeletedEvent message)
        {
            _listPeers.DeleteForPeer(message.PeerId);
        }

        private static OfferItem Describe(CatalogFile file, MediaKind kind, string id)
        {
            return new OfferItem
            {
                Id = id,
                Kind = kind,

                // Nothing of the path is sent, not even the file name.
                SceneName = file.SceneName.IsNotNullOrWhiteSpace() ? file.SceneName : null,
                Edition = (file as CatalogMovieFile)?.Edition,
                Extension = ExtensionOf(file.RelativePath),
                Quality = file.Quality,
                QualitySource = file.QualitySource,
                Resolution = file.Resolution,
                Size = file.Size,
                Languages = file.Languages,
                CustomFormats = file.CustomFormats,
                ReleaseGroup = file.ReleaseGroup,
                VideoCodec = file.VideoCodec,
                VideoDynamicRange = file.VideoDynamicRange,
                AudioCodec = file.AudioCodec,
                AudioChannels = file.AudioChannels,
                DateAdded = file.DateAdded
            };
        }

        // The numbers Sonarr would search for these episodes by: the scene's where it has them,
        // TVDB's where it does not. Nothing when that is just the TVDB numbers, and nothing when
        // the scene puts the episodes of one file in different seasons, which a title cannot say.
        private static (int? Season, List<int> Episodes) SceneNumbers(List<CatalogEpisode> contained)
        {
            if (!contained.Any(e => e.SceneSeasonNumber.HasValue || e.SceneEpisodeNumber.HasValue))
            {
                return (null, null);
            }

            var seasons = contained.Select(e => e.SceneSeasonNumber ?? e.SeasonNumber).Distinct().ToList();
            var episodes = contained.Select(e => e.SceneEpisodeNumber ?? e.EpisodeNumber).ToList();

            if (seasons.Count != 1 || (seasons[0] == contained[0].SeasonNumber && episodes.SequenceEqual(contained.Select(e => e.EpisodeNumber))))
            {
                return (null, null);
            }

            return (seasons[0], episodes);
        }

        // The extension says what kind of file it is. It is the only part of the file name sent.
        private static string ExtensionOf(string path)
        {
            var name = (path ?? string.Empty).Split('/', '\\').Last();
            var dot = name.LastIndexOf('.');
            var extension = dot > 0 ? name[dot..].ToLowerInvariant() : null;

            return extension != null && extension.Length is > 1 and <= 6 && extension.Skip(1).All(char.IsAsciiLetterOrDigit) ? extension : null;
        }

        private static Offer Seal(List<OfferItem> items)
        {
            return Offer.Seal(items);
        }

        private void EnsureHiddenList()
        {
            if (!_lists.All().Any(l => l.IsHidden))
            {
                _lists.Insert(new ShareList { Name = HiddenListName, IsHidden = true });
            }
        }
    }
}

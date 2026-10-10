using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Sharing
{
    public interface ISharingService
    {
        List<ShareListDetails> All();
        ShareListDetails Create(string name);
        ShareListDetails Update(int id, string name, IEnumerable<TitleKey> items, IEnumerable<int> peerIds);
        void Delete(int id);
        List<ShareableTitle> GetTitles();
        Peer SetShareAll(int peerId, bool shareAll);
        Offer BuildOffer(Peer peer);
    }

    // Decides what each peer is told about. The rule, in order:
    //   1. Nothing is offered to a peer that is not trusted.
    //   2. Anything on the Hidden list is offered to nobody.
    //   3. A peer marked "share everything" is offered the rest.
    //   4. Otherwise a peer is offered what is on the share lists it has been given.
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
                              IEventAggregator eventAggregator)
        {
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

        public ShareListDetails Update(int id, string name, IEnumerable<TitleKey> items, IEnumerable<int> peerIds)
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
            _items.InsertMany((items ?? Enumerable.Empty<TitleKey>()).Distinct().Select(k => new ShareListItem
            {
                ShareListId = id,
                MediaKind = k.Kind,
                ApplicationId = k.ApplicationId,
                ArrId = k.ArrId
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

            var lists = All();
            var hidden = lists.Where(l => l.List.IsHidden).SelectMany(l => l.Items).ToHashSet();
            var listed = lists.Where(l => !l.List.IsHidden && l.PeerIds.Contains(peer.Id)).SelectMany(l => l.Items).ToHashSet();

            bool IsShared(TitleKey key) => !hidden.Contains(key) && (peer.ShareAll || listed.Contains(key));

            var offered = new List<OfferItem>();

            var series = _series.All()
                .Where(s => s.MissingSince == null && IsShared(new TitleKey(MediaKind.Tv, s.ApplicationId, s.ArrId)))
                .ToDictionary(s => (s.ApplicationId, s.ArrId));

            if (series.Count > 0)
            {
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
                    if (contained.Count == 0)
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
                    offered.Add(item);
                }
            }

            var movies = _movies.All()
                .Where(m => m.MissingSince == null && IsShared(new TitleKey(MediaKind.Movie, m.ApplicationId, m.ArrId)))
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
                    offered.Add(item);
                }
            }

            return Seal(offered);
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

                // The file name is the only part of the path a peer ever sees, and only as a release title.
                ReleaseTitle = file.SceneName.IsNotNullOrWhiteSpace() ? file.SceneName : FileNameOnly(file.RelativePath),
                Quality = file.Quality,
                QualitySource = file.QualitySource,
                Resolution = file.Resolution,
                Size = file.Size,
                Languages = file.Languages,
                CustomFormats = file.CustomFormats,
                ReleaseGroup = file.ReleaseGroup,
                VideoCodec = file.VideoCodec,
                AudioCodec = file.AudioCodec,
                DateAdded = file.DateAdded
            };
        }

        // The Arr app may run on another OS, so both kinds of separator end the folder part.
        private static string FileNameOnly(string path)
        {
            var name = (path ?? string.Empty).Split('/', '\\').Last();
            var extension = name.LastIndexOf('.');

            return extension > 0 ? name[..extension] : name;
        }

        private static Offer Seal(List<OfferItem> items)
        {
            var ordered = items.OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
            var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(ordered));

            return new Offer { Version = Convert.ToHexString(hash)[..16], Items = ordered };
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

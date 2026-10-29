using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Posters
{
    // Answers a trusted friend that asks for the poster of a title it is offered.
    public interface IPosterExchange
    {
        PeerReply Answer(Peer peer, PeerMessage request);
    }

    // Posters of titles in this node's own library, from its Arr apps.
    public interface ILocalPosters
    {
        byte[] Get(PosterTitle title);
    }

    // Posters of titles in friends' catalogs, from the friends that offer them.
    public interface IPeerPosters
    {
        byte[] Get(int peerCatalogItemId);
    }

    // A friend is offered the poster of a title exactly when it is offered a file of the title, by
    // the same rule. The poster comes from this node's Arr app, which downloaded it when it added
    // the title. Nothing else is contacted.
    public class PosterExchange : IPosterExchange
    {
        private readonly ISharingService _sharing;
        private readonly ILocalPosters _local;

        public PosterExchange(ISharingService sharing, ILocalPosters local)
        {
            _sharing = sharing;
            _local = local;
        }

        public PeerReply Answer(Peer peer, PeerMessage request)
        {
            var title = _sharing.PosterFor(peer, request.PosterFor);

            // One answer for "not offered" and "no poster", so a friend learns nothing by asking.
            return new PeerReply { Ok = true, Poster = title == null ? null : _local.Get(title) };
        }
    }

    public class LocalPosters : ILocalPosters
    {
        // The Arr apps rarely change a poster. A title without one may get one when its app next
        // refreshes it.
        public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);
        public static readonly TimeSpan NoneFor = TimeSpan.FromDays(1);

        private readonly IApplicationFactory _applicationFactory;
        private readonly PosterCache _cache;
        private readonly Logger _logger;

        public LocalPosters(IApplicationFactory applicationFactory, IAppFolderInfo appFolderInfo, Logger logger)
        {
            _applicationFactory = applicationFactory;
            _cache = new PosterCache(Path.Combine(appFolderInfo.GetAppDataPath(), "Posters", "Local"));
            _logger = logger;
        }

        public byte[] Get(PosterTitle title)
        {
            var key = $"{title.ApplicationId}-{title.Subject.ToString().ToLowerInvariant()}-{title.ArrId}";

            if (_cache.TryGet(key, KeepFor, out var cached))
            {
                return cached;
            }

            var definition = _applicationFactory.Find(title.ApplicationId);

            if (definition == null || _applicationFactory.GetInstance(definition) is not IPosterSource source)
            {
                return null;
            }

            try
            {
                var poster = source.GetPoster(title.Subject, title.ArrId);

                if (poster == null)
                {
                    _cache.PutNone(key, NoneFor);
                }
                else
                {
                    _cache.Put(key, poster);
                }

                return poster;
            }
            catch (Exception ex) when (ex is HttpException or System.Net.Http.HttpRequestException or IOException or Newtonsoft.Json.JsonException)
            {
                // The app is down for now. Nothing is remembered, so the next request asks again.
                _logger.Debug(ex, "Unable to get the poster of {0} {1} from {2}", title.Subject, title.ArrId, definition.Name);

                return null;
            }
        }
    }

    public class PeerPosters : IPeerPosters
    {
        public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
        public static readonly TimeSpan NoneFor = TimeSpan.FromDays(1);

        // A friend that could not be reached is asked again after this.
        public static readonly TimeSpan UnreachableFor = TimeSpan.FromMinutes(15);

        // A page of a friend's catalog asks for many posters at once. Only a few go to friends at
        // a time, and one title is asked for once however many of its files are on the page.
        private static readonly SemaphoreSlim Asking = new(4);
        private static readonly ConcurrentDictionary<string, object> Locks = new();

        private readonly IPeerCatalogRepository _items;
        private readonly IPeerRepository _peers;
        private readonly IPeerService _peerService;
        private readonly PosterCache _cache;
        private readonly Logger _logger;

        public PeerPosters(IPeerCatalogRepository items, IPeerRepository peers, IPeerService peerService, IAppFolderInfo appFolderInfo, Logger logger)
        {
            _items = items;
            _peers = peers;
            _peerService = peerService;
            _cache = new PosterCache(Path.Combine(appFolderInfo.GetAppDataPath(), "Posters", "Friends"));
            _logger = logger;
        }

        public byte[] Get(int peerCatalogItemId)
        {
            var item = _items.Find(peerCatalogItemId);
            var peer = item == null ? null : _peers.Find(item.PeerId);

            if (peer is not { State: PeerState.Trusted })
            {
                return null;
            }

            var key = KeyOf(item);

            if (_cache.TryGet(key, KeepFor, out var cached))
            {
                return cached;
            }

            lock (Locks.GetOrAdd(key, _ => new object()))
            {
                // Another request for the same title may have just fetched it.
                if (_cache.TryGet(key, KeepFor, out cached))
                {
                    return cached;
                }

                Asking.Wait();

                try
                {
                    var reply = _peerService.Ask(peer, new PeerMessage { Type = PeerMessageTypes.Poster, PosterFor = item.RemoteId });
                    var poster = reply is { Ok: true } ? PosterImages.Checked(reply.Poster) : null;

                    if (poster == null)
                    {
                        _cache.PutNone(key, NoneFor);
                    }
                    else
                    {
                        _cache.Put(key, poster);
                    }

                    return poster;
                }
                catch (Exception ex) when (ex is TransportException or PeerException)
                {
                    // An older Nodearr refuses the question, one that is away does not answer, and
                    // one reached only through a relay cannot send a poster at all.
                    _logger.Debug(ex, "Unable to get a poster from {0}: {1}", peer.Name, ex is TransportException { Failure: TransportFailure.NoDirectRoute } ? TransportException.NoDirectRouteReason : ex.Message);
                    _cache.PutNone(key, UnreachableFor);

                    return null;
                }
                finally
                {
                    Asking.Release();
                }
            }
        }

        // What the poster is of, the same whichever friend offers it: a series by TVDB, a movie by
        // TMDb or IMDb, adult content by Whisparr's IDs, an album by its MusicBrainz release group.
        // A title known by none of those is kept apart for each friend.
        public static string KeyOf(PeerCatalogItem item)
        {
            if (item.IsAdult)
            {
                var id = item.StashId ?? item.TpdbId ?? item.ForeignId;

                if (id != null)
                {
                    return $"adult-{id}";
                }
            }
            else if (item.MediaKind == MediaKind.Tv && item.TvdbId > 0)
            {
                return $"tv-{item.TvdbId}";
            }
            else if (item.MediaKind == MediaKind.Movie && item.TmdbId > 0)
            {
                return $"movie-{item.TmdbId}";
            }
            else if (item.MediaKind == MediaKind.Movie && item.ImdbId != null)
            {
                return $"movie-{item.ImdbId}";
            }
            else if (item.MediaKind == MediaKind.Music && item.ForeignAlbumId != null)
            {
                return $"album-{item.ForeignAlbumId}";
            }

            return $"peer{item.PeerId}-{item.RemoteId}";
        }
    }
}

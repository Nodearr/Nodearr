using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Transfers
{
    public class HashingStatus
    {
        // Files shared with at least one trusted node, that can be read here.
        public int Shared { get; set; }

        // How many of those have a hash for the version of the file that is there now.
        public int Hashed { get; set; }

        // The file being read now, if one is.
        public string Current { get; set; }

        // Waiting for this node to finish sending, so as not to slow it down.
        public bool WaitingForUploads { get; set; }
        public DateTime? LastPass { get; set; }
    }

    public interface IFileHasher
    {
        // The hash kept for the file at a path, or null. The transport checks it is still current.
        HashedFile Known(string path);
        HashingStatus Status();

        // Goes through every shared file once. Returns false if it had to stop early.
        bool HashShared(CancellationToken cancellation);
    }

    // Works out the hash of every shared file before anyone asks for it, so that a request does
    // not wait while a large file is read (plan decision 3). A request that comes first still
    // has the file hashed then, and the two never read the same file at once.
    //
    // One file at a time, newest first, and only while this node is not sending anything. Hashes
    // are kept, so a file is read again only when it changes.
    public class FileHasher : IFileHasher,
                              IHandle<ApplicationStartedEvent>,
                              IHandle<ApplicationShutdownRequested>,
                              IHandleAsync<CatalogRefreshedEvent>,
                              IHandleAsync<SharingChangedEvent>,
                              IHandleAsync<PeerStateChangedEvent>
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
        private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan UploadCheck = TimeSpan.FromSeconds(5);

        // A hash for a file that is no longer shared is kept this long, in case it is shared again.
        private static readonly TimeSpan KeepUnshared = TimeSpan.FromDays(30);

        private readonly IPeerRepository _peers;
        private readonly ISharingService _sharing;
        private readonly ICatalogFileLocator _locator;
        private readonly IFileHashRepository _hashes;
        private readonly IFileTransport _transport;
        private readonly IUploadTracker _uploads;
        private readonly Logger _logger;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly AutoResetEvent _wake = new(false);
        private readonly object _lock = new();

        private Thread _thread;
        private HashingStatus _status = new();

        public FileHasher(IPeerRepository peers,
                          ISharingService sharing,
                          ICatalogFileLocator locator,
                          IFileHashRepository hashes,
                          IFileTransport transport,
                          IUploadTracker uploads,
                          Logger logger)
        {
            _peers = peers;
            _sharing = sharing;
            _locator = locator;
            _hashes = hashes;
            _transport = transport;
            _uploads = uploads;
            _logger = logger;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            lock (_lock)
            {
                if (_thread != null)
                {
                    return;
                }

                _thread = new Thread(Run) { IsBackground = true, Name = "hashing" };
                _thread.Start();
            }
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            _shutdown.Cancel();
        }

        // Something may be shared now that was not, or be a new file.
        public void HandleAsync(CatalogRefreshedEvent message) => _wake.Set();
        public void HandleAsync(SharingChangedEvent message) => _wake.Set();

        public void HandleAsync(PeerStateChangedEvent message)
        {
            if (message.Peer.State == PeerState.Trusted)
            {
                _wake.Set();
            }
        }

        public HashedFile Known(string path)
        {
            return _hashes.FindByPath(path)?.ToHashedFile();
        }

        public HashingStatus Status()
        {
            var status = _status;

            return new HashingStatus
            {
                Shared = status.Shared,
                Hashed = status.Hashed,
                Current = status.Current,
                WaitingForUploads = status.WaitingForUploads,
                LastPass = status.LastPass
            };
        }

        public bool HashShared(CancellationToken cancellation)
        {
            var paths = SharedPaths();
            var kept = _hashes.All().ToDictionary(h => h.Path, StringComparer.Ordinal);
            var status = new HashingStatus { Shared = paths.Count, Hashed = paths.Count(kept.ContainsKey), LastPass = _status.LastPass };

            _status = status;

            foreach (var path in paths)
            {
                if (cancellation.IsCancellationRequested)
                {
                    return false;
                }

                kept.TryGetValue(path, out var known);

                // A file with no hash yet has to be read. That waits until nothing is being sent.
                if (known == null && !WaitForUploads(status, cancellation))
                {
                    return false;
                }

                status.Current = path;

                HashedFile hashed;

                try
                {
                    hashed = _transport.Hash(path, known?.ToHashedFile());
                }
                catch (TransportException ex)
                {
                    _logger.Debug(ex, "Stopped hashing: the transport is not available");
                    status.Current = null;

                    return false;
                }

                if (hashed == null)
                {
                    continue;
                }

                if (known == null)
                {
                    _hashes.Insert(new FileHash { Path = path, Size = hashed.Size, ModifiedNs = hashed.ModifiedNs, Blake3 = hashed.Blake3, Hashed = DateTime.UtcNow });
                    status.Hashed++;
                    _logger.Debug("Hashed {0}", path);
                }
                else if (!known.Matches(hashed))
                {
                    // The file changed since it was last hashed.
                    known.Size = hashed.Size;
                    known.ModifiedNs = hashed.ModifiedNs;
                    known.Blake3 = hashed.Blake3;
                    known.Hashed = DateTime.UtcNow;
                    _hashes.Update(known);
                    _logger.Debug("Hashed {0} again, because it changed", path);
                }
            }

            status.Current = null;
            status.LastPass = DateTime.UtcNow;

            var shared = paths.ToHashSet(StringComparer.Ordinal);
            var stale = kept.Values.Where(h => !shared.Contains(h.Path) && h.Hashed < DateTime.UtcNow - KeepUnshared).Select(h => h.Id).ToList();

            if (stale.Count > 0)
            {
                _hashes.DeleteMany(stale);
            }

            return true;
        }

        private void Run()
        {
            var wait = TimeSpan.FromSeconds(10);

            // Woken early by a change, or after the interval to catch files changed in place.
            while (WaitHandle.WaitAny(new[] { _shutdown.Token.WaitHandle, _wake }, wait) != 0)
            {
                try
                {
                    wait = HashShared(_shutdown.Token) ? Interval : RetryAfter;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to hash shared files");
                    wait = RetryAfter;
                }
            }
        }

        private bool WaitForUploads(HashingStatus status, CancellationToken cancellation)
        {
            while (_uploads.All().Any(u => u.IsActive))
            {
                status.WaitingForUploads = true;

                if (cancellation.WaitHandle.WaitOne(UploadCheck))
                {
                    return false;
                }
            }

            status.WaitingForUploads = false;

            return true;
        }

        // Every file offered to at least one trusted node, where it is on this machine, newest first.
        private List<string> SharedPaths()
        {
            var offered = new Dictionary<string, DateTime?>();

            foreach (var peer in _peers.All().Where(p => p.State == PeerState.Trusted))
            {
                foreach (var item in _sharing.BuildOffer(peer).Items)
                {
                    offered.TryAdd(item.Id, item.DateAdded);
                }
            }

            var located = _locator.Locate(offered.Keys);

            return offered
                .OrderByDescending(o => o.Value ?? DateTime.MinValue)
                .Select(o => located.GetValueOrDefault(o.Key))
                .Where(l => l is { Readable: true })
                .Select(l => l.LocalPath)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }
}

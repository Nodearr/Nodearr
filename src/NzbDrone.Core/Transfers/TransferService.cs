using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Transfers
{
    public interface ITransferService
    {
        // Starts fetching what a ticket names. Returns null when no trusted node offers it any more.
        TransferJob Add(string candidateId, string category);

        // The same, for a ticket that was found by the day the episode aired.
        TransferJob Add(string candidateId, string category, string airDate);

        // Tries a failed transfer again, as a new one. Returns null when that cannot be done.
        TransferJob Retry(string downloadId);
        bool Remove(string downloadId, bool deleteFiles);
        TransferJob Find(string downloadId);
        List<TransferJob> All();

        // Moves every transfer on by one step. Called once a second while Nodearr runs.
        void Tick();
    }

    // The life of a transfer:
    //
    //   Queued -> Downloading -> Completed
    //                |  ^
    //                v  |  a failure that may pass, while attempts remain
    //              Queued
    //                |
    //                v  any other failure, or no attempts left
    //              Failed
    //
    // The transport does the moving and the checking. This decides what to ask of it, and keeps
    // the record the Arr apps read through the download client API.
    //
    // An album is one transfer of several parts, its tracks. They arrive one after another into
    // the same folder, each verified as a single file is, and each with its own attempts. The
    // folder moves into place only once the last one is verified (ADR 0020).
    public class TransferService : ITransferService, IHandle<PeerStateChangedEvent>, IHandle<PeerDeletedEvent>,
                                   IHandleAsync<PeerTransportAddressChangedEvent>, IHandleAsync<PeerAddressChangedEvent>
    {
        public const int MaxAttempts = 3;

        // Folders named alike waiting under complete/ before one more is refused.
        private const int MaxSameNamedFolders = 100;

        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

        private readonly ITransferRepository _jobs;
        private readonly ICandidateService _candidates;
        private readonly IPeerRepository _peers;
        private readonly IFileTransport _transport;
        private readonly ITransferFolders _folders;
        private readonly IRouteDiagnosis _routes;
        private readonly INodeSettingsProvider _settings;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly object _sync = new();

        public TransferService(ITransferRepository jobs,
                               ICandidateService candidates,
                               IPeerRepository peers,
                               IFileTransport transport,
                               ITransferFolders folders,
                               IRouteDiagnosis routes,
                               INodeSettingsProvider settings,
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            _jobs = jobs;
            _candidates = candidates;
            _peers = peers;
            _transport = transport;
            _folders = folders;
            _routes = routes;
            _settings = settings;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public TransferJob Add(string candidateId, string category)
        {
            return Add(candidateId, category, null);
        }

        public TransferJob Add(string candidateId, string category, string airDate)
        {
            lock (_sync)
            {
                // An Arr app that sends the same ticket twice gets the same download back.
                var existing = _jobs.Active().FirstOrDefault(j => j.CandidateId == candidateId);

                if (existing != null)
                {
                    return existing;
                }

                var candidate = _candidates.Find(candidateId);
                var peer = candidate == null ? null : _peers.Find(candidate.PeerId);

                if (peer == null)
                {
                    return null;
                }

                // Only a day the file really has an episode for changes the title.
                airDate = airDate != null && candidate.GetAirDates().Contains(airDate) ? airDate : null;

                var parts = candidate.IsAlbum ? Parts(candidate) : null;

                // An album offered without the tracks to fetch has nothing that can be fetched.
                if (candidate.MediaKind == MediaKind.Music && parts == null)
                {
                    return null;
                }

                var job = new TransferJob
                {
                    DownloadId = "SABnzbd_nzo_" + Guid.NewGuid().ToString("N")[..12],
                    CandidateId = candidateId,
                    PeerId = peer.Id,
                    PeerNodeId = peer.NodeId,
                    RemoteId = candidate.RemoteId,
                    MediaKind = candidate.MediaKind,
                    Title = airDate == null ? candidate.ReleaseTitle : ReleaseTitleBuilder.For(candidate, airDate),
                    AirDate = airDate,
                    Extension = candidate.Extension,
                    Category = category,
                    Size = candidate.Size,
                    Blake3 = candidate.Blake3,
                    State = TransferState.Queued,
                    Added = DateTime.UtcNow
                };

                if (parts != null)
                {
                    job.SetParts(parts);
                    job.RemoteId = parts[0].RemoteId;
                    job.Extension = parts[0].Extension;
                    job.Size = parts.Sum(p => p.Size);

                    // The hash is each part's. The album as a whole has none.
                    job.Blake3 = null;
                }

                job = _jobs.Insert(job);

                _logger.Info("Queued {0} from {1} as {2}", job.Title, peer.Name, job.DownloadId);
                Changed();

                return job;
            }
        }

        public TransferJob Retry(string downloadId)
        {
            lock (_sync)
            {
                var failed = _jobs.FindByDownloadId(downloadId);

                if (failed is not { State: TransferState.Failed })
                {
                    return null;
                }

                var again = Add(failed.CandidateId, failed.Category, failed.AirDate);

                if (again != null)
                {
                    _jobs.Delete(failed.Id);
                }

                return again;
            }
        }

        public bool Remove(string downloadId, bool deleteFiles)
        {
            lock (_sync)
            {
                var job = _jobs.FindByDownloadId(downloadId);

                if (job == null)
                {
                    return false;
                }

                if (job.State == TransferState.Downloading)
                {
                    Forget(job);
                }

                DeleteFolder(_folders.IncompleteFolder(job));

                if (deleteFiles && job.OutputPath != null)
                {
                    DeleteFolder(job.OutputPath);
                }

                _jobs.Delete(job.Id);
                _logger.Info("Removed transfer {0} ({1})", job.DownloadId, job.Title);
                Changed();

                return true;
            }
        }

        public TransferJob Find(string downloadId)
        {
            return _jobs.FindByDownloadId(downloadId);
        }

        public List<TransferJob> All()
        {
            return _jobs.All().OrderBy(j => j.Added).ToList();
        }

        public void Tick()
        {
            lock (_sync)
            {
                var active = _jobs.Active();

                if (active.Count == 0)
                {
                    return;
                }

                Dictionary<string, FileTransferProgress> progress;

                try
                {
                    progress = _transport.List().ToDictionary(p => p.Id);
                }
                catch (TransportException)
                {
                    // The transport is not running. Everything waits for it.
                    return;
                }

                var changed = false;

                foreach (var job in active.Where(j => j.State == TransferState.Downloading))
                {
                    changed |= Follow(job, progress.GetValueOrDefault(job.TransportId));
                }

                // Read on every tick, so a new limit applies at once. Lowering it lets what is
                // already running finish; nothing is stopped part way.
                var limit = _settings.Get().MaxDownloads;
                var running = active.Count(j => j.State == TransferState.Downloading);

                // In the order the Arr apps asked. A transfer waiting to be tried again keeps its
                // place, as it had already been given a slot.
                foreach (var job in active.Where(j => j.State == TransferState.Queued).OrderBy(j => j.Added))
                {
                    if (limit > 0 && running >= limit)
                    {
                        break;
                    }

                    if (job.NextAttempt.HasValue && job.NextAttempt.Value > DateTime.UtcNow)
                    {
                        continue;
                    }

                    changed |= Begin(job);
                    running += job.State == TransferState.Downloading ? 1 : 0;
                }

                if (changed)
                {
                    Changed();
                }
            }
        }

        // The node this file was coming from is no longer trusted. Nothing more is fetched from it,
        // and nothing more is sent to it.
        public void Handle(PeerStateChangedEvent message)
        {
            if (message.Peer.State == PeerState.Trusted)
            {
                return;
            }

            EndTrust(message.Peer.Id, message.Peer.NodeId, message.Peer.State == PeerState.Blocked ? TransferFailure.Blocked : TransferFailure.Revoked);
        }

        public void Handle(PeerDeletedEvent message)
        {
            EndTrust(message.PeerId, null, TransferFailure.Revoked);
        }

        public void HandleAsync(PeerTransportAddressChangedEvent message)
        {
            ReopenAfterAddressChange(null);
        }

        public void HandleAsync(PeerAddressChangedEvent message)
        {
            ReopenAfterAddressChange(message.PeerId);
        }

        private void ReopenAfterAddressChange(int? peerId)
        {
            lock (_sync)
            {
                var affected = _jobs.Active().Where(j => j.State == TransferState.Downloading && (!peerId.HasValue || j.PeerId == peerId.Value)).ToList();
                if (affected.Count == 0)
                {
                    return;
                }

                var progress = new Dictionary<string, FileTransferProgress>();
                try
                {
                    progress = _transport.List().ToDictionary(p => p.Id);
                }
                catch (TransportException ex)
                {
                    _logger.Debug(ex, "Unable to read transfer progress after the network address changed");
                }

                foreach (var job in affected)
                {
                    // Keep the latest hash and offset, and let an already completed or failed
                    // attempt follow its usual path instead of reopening it.
                    if (progress.TryGetValue(job.TransportId, out var current))
                    {
                        Follow(job, current);
                        if (job.State != TransferState.Downloading)
                        {
                            continue;
                        }
                    }

                    Forget(job);
                    job.State = TransferState.Queued;
                    job.WaitingPosition = null;
                    job.NextAttempt = DateTime.UtcNow.Add(RetryDelay);

                    // Moving networks is a voluntary reconnect, not a failed attempt. Repeated
                    // announcements while queued do not spend the retry budget or delay it again.
                    job.Attempts = Math.Max(0, job.Attempts - 1);
                    _jobs.Update(job);
                    _logger.Info("Reopening transfer {0} from its saved bytes after a network address change", job.DownloadId);
                }

                Changed();
            }
        }

        private void EndTrust(int peerId, string nodeId, TransferFailure failure)
        {
            lock (_sync)
            {
                var affected = _jobs.Active().Where(j => j.PeerId == peerId).ToList();

                foreach (var job in affected)
                {
                    if (job.State == TransferState.Downloading)
                    {
                        Forget(job);
                    }

                    // Another node with the same file is not affected by this one.
                    if (!SwitchToAnotherCopy(job, failure, null))
                    {
                        Fail(job, failure, null);
                    }
                }

                nodeId ??= affected.FirstOrDefault()?.PeerNodeId;

                if (nodeId != null)
                {
                    try
                    {
                        _transport.Disconnect(nodeId);
                    }
                    catch (TransportException ex)
                    {
                        _logger.Debug(ex, "Unable to cut transfers to node {0}", nodeId);
                    }
                }

                if (affected.Count > 0)
                {
                    Changed();
                }
            }
        }

        private bool Begin(TransferJob job)
        {
            var peer = _peers.Find(job.PeerId);

            if (peer is not { State: PeerState.Trusted })
            {
                Fail(job, TransferFailure.Revoked, null);

                return true;
            }

            try
            {
                _transport.Start(new FileTransferRequest
                {
                    Id = job.TransportId,
                    Peer = peer.ToAddress(),
                    File = job.RemoteId,
                    Destination = _folders.IncompleteFile(job),
                    ExpectedHash = job.Hash
                });
            }
            catch (TransportException)
            {
                return false;
            }

            job.State = TransferState.Downloading;
            job.Attempts++;
            job.NextAttempt = null;
            job.WaitingPosition = null;
            job.Started ??= DateTime.UtcNow;
            _jobs.Update(job);

            return true;
        }

        // Brings a job up to date with what the transport says. Returns whether anything changed.
        private bool Follow(TransferJob job, FileTransferProgress progress)
        {
            if (progress == null)
            {
                // The transport was restarted and knows nothing of this. Ask again. What had
                // arrived is still on disk, and the hash says whether it can be carried on from.
                job.State = TransferState.Queued;
                job.Attempts = Math.Max(0, job.Attempts - 1);
                job.WaitingPosition = null;
                _jobs.Update(job);

                return true;
            }

            var before = (job.BytesDone, job.Size, job.Hash, job.WaitingPosition);
            job.WaitingPosition = progress.State == FileTransferState.Waiting ? progress.QueuePosition ?? 1 : null;

            // An album's size is all of its parts', as offered.
            if (progress.Size > 0 && !job.HasParts)
            {
                job.Size = progress.Size;
            }

            // While it is still connecting, or waiting its turn at the other node, the transport has
            // counted nothing yet. What the job already holds stays on show until the transport
            // says where it carried on from.
            if (progress.State is not (FileTransferState.Connecting or FileTransferState.Waiting))
            {
                job.BytesDone = PartsDone(job) + progress.BytesDone;
            }

            job.Hash = progress.Hash ?? job.Hash;

            switch (progress.State)
            {
                case FileTransferState.Completed when job.HasParts && job.PartIndex < job.GetParts().Count - 1:
                    Forget(job);
                    NextPart(job);
                    AttemptEnded(job, TransferFailure.None);

                    return true;

                case FileTransferState.Completed:
                    Forget(job);
                    Complete(job);
                    AttemptEnded(job, job.State == TransferState.Completed ? TransferFailure.None : job.Failure);

                    return true;

                case FileTransferState.Failed:
                    var failure = TransferFailureText.FromTransport(progress.FailureKind);

                    // The node this attempt was with, before the transfer can move to another.
                    var peerId = job.PeerId;

                    Forget(job);
                    Failed(job, failure, progress.FailureMessage);
                    _eventAggregator.PublishEvent(new TransferAttemptEndedEvent(peerId, failure));

                    return true;

                default:
                    if (before == (job.BytesDone, job.Size, job.Hash, job.WaitingPosition))
                    {
                        return false;
                    }

                    _jobs.SetFields(job, j => j.BytesDone, j => j.Size, j => j.Hash, j => j.WaitingPosition);

                    return true;
            }
        }

        // An album's tracks, each named as Lidarr names its own files: the artist, the album, the
        // track's number, with its disc's when the album has more than one, and its title. A
        // Lidarr that does not rename keeps these names, every album's in the artist's folder.
        private static List<TransferPart> Parts(PeerCatalogItem album)
        {
            var tracks = album.GetTracks();
            var discs = tracks.Select(t => t.Disc ?? 1).Distinct().Count();
            var width = tracks.Count >= 100 ? 3 : 2;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return tracks.Select((track, index) =>
            {
                string name = null;

                if (track.Title != null)
                {
                    var number = (track.Number ?? index + 1).ToString().PadLeft(width, '0');
                    var place = discs > 1 ? $"{track.Disc ?? 1}-{number}" : number;

                    name = $"{album.Artist} - {album.Title} - {place} - {track.Title}";

                    // Two tracks of one album named alike would overwrite each other.
                    for (var copy = 2; !taken.Add(name); copy++)
                    {
                        name = $"{album.Artist} - {album.Title} - {place} - {track.Title} ({copy})";
                    }
                }

                return new TransferPart { RemoteId = track.Id, Extension = track.Extension, Size = track.Size, Name = name };
            }).ToList();
        }

        // The bytes of an album's parts that have already arrived and been verified.
        private static long PartsDone(TransferJob job)
        {
            return job.HasParts ? job.GetParts().Take(job.PartIndex).Sum(p => p.Size) : 0;
        }

        // One part of an album is verified. The next is fetched as soon as a slot is free, with
        // attempts of its own.
        private void NextPart(TransferJob job)
        {
            var parts = job.GetParts();

            job.PartIndex++;
            job.RemoteId = parts[job.PartIndex].RemoteId;
            job.Extension = parts[job.PartIndex].Extension;
            job.Hash = null;
            job.BytesDone = PartsDone(job);
            job.Attempts = 0;
            job.State = TransferState.Queued;
            job.NextAttempt = null;
            _jobs.Update(job);
        }

        // The file is verified. It appears under complete/ in one step, or not at all.
        private void Complete(TransferJob job)
        {
            var from = _folders.IncompleteFolder(job);
            string to;

            try
            {
                to = MoveToUnusedFolder(from, _folders.CompleteFolder(job));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "Unable to move {0} into place", job.Title);
                Fail(job, TransferFailure.DiskError, ex.Message);

                return;
            }

            job.State = TransferState.Completed;
            job.Failure = TransferFailure.None;
            job.FailureMessage = null;
            job.BytesDone = job.Size;
            job.OutputPath = to;
            job.Ended = DateTime.UtcNow;
            _jobs.Update(job);
            _logger.Info("Transfer {0} completed: {1}", job.DownloadId, job.Title);
        }

        // A folder already there is another download's, until its Arr app has imported it: two
        // releases can be named alike, and a peer chooses the name. As SABnzbd does, this one goes
        // next to it as Title.1, Title.2 and so on, and the Arr app is told the folder it is in.
        private static string MoveToUnusedFolder(string from, string folder)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(folder));

            for (var n = 0; n < MaxSameNamedFolders; n++)
            {
                var to = n == 0 ? folder : $"{folder}.{n}";

                if (Directory.Exists(to) || File.Exists(to))
                {
                    continue;
                }

                try
                {
                    Directory.Move(from, to);

                    return to;
                }
                catch (IOException) when (Directory.Exists(to) || File.Exists(to))
                {
                    // Another transfer of the same name took it a moment before.
                }
            }

            throw new IOException($"{MaxSameNamedFolders} folders named {Path.GetFileName(folder)} are waiting to be imported already");
        }

        private void Failed(TransferJob job, TransferFailure failure, string detail)
        {
            if (SwitchToAnotherCopy(job, failure, detail))
            {
                return;
            }

            var startsOver = TransferFailureText.StartsOver(failure);

            if ((TransferFailureText.IsTransient(failure) || startsOver) && job.Attempts < MaxAttempts)
            {
                _logger.Info("Transfer {0} stopped ({1}: {2}). Trying again, attempt {3} of {4}", job.DownloadId, failure, detail, job.Attempts + 1, MaxAttempts);

                if (startsOver)
                {
                    // The transport has already thrown the file away. Nothing here is worth
                    // resuming from, but an album's parts that were verified are kept.
                    if (job.HasParts)
                    {
                        DeleteFile(_folders.IncompleteFile(job));
                    }
                    else
                    {
                        DeleteFolder(_folders.IncompleteFolder(job));
                    }

                    job.Hash = null;
                    job.BytesDone = PartsDone(job);
                }

                job.State = TransferState.Queued;
                job.NextAttempt = DateTime.UtcNow.Add(RetryDelay);
                job.Failure = failure;
                job.FailureMessage = Explain(job, failure, detail);
                _jobs.Update(job);

                return;
            }

            Fail(job, failure, detail);
        }

        // When the node a file was coming from fails it, another trusted node that offers a file
        // with the same hash has the same bytes. The transfer moves to it at once, and carries on
        // from what has arrived, since that is the start of the same file. Each node is moved to
        // once and has its own attempts. A node already directly reachable is tried first.
        //
        // A file that did not match its hash is tried again from the same node first, since that
        // node is the one that must work its hash out again. A disk error here is no node's doing.
        //
        // An album stays with the node it came from, whose parts it is made of.
        private bool SwitchToAnotherCopy(TransferJob job, TransferFailure failure, string detail)
        {
            var hash = job.Hash ?? job.Blake3;

            if (hash == null || job.HasParts || failure is TransferFailure.None or TransferFailure.VerificationFailed or TransferFailure.DiskError)
            {
                return false;
            }

            var tried = job.GetTriedPeerIds();
            tried.Add(job.PeerId);

            var peers = _peers.All().Where(p => p.State == PeerState.Trusted && !tried.Contains(p.Id)).ToDictionary(p => p.Id);
            var copy = _candidates.Copies(hash)
                .Where(c => peers.ContainsKey(c.PeerId) && c.Size == job.Size)
                .OrderByDescending(c => peers[c.PeerId].ConnectionPath == ConnectionPath.Direct)
                .ThenBy(c => c.CandidateId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (copy == null)
            {
                return false;
            }

            var from = _peers.Find(job.PeerId)?.Name ?? "the other node";
            var to = peers[copy.PeerId];

            // Said of the node that failed, so worked out before the transfer leaves it. The detail
            // can be the transport's own words, which end without a full stop.
            var explained = Explain(job, failure, detail).TrimEnd();

            explained = explained.EndsWith('.') ? explained : explained + ".";

            job.TriedPeerIds = string.Join(",", tried.OrderBy(id => id));
            job.PeerId = to.Id;
            job.PeerNodeId = to.NodeId;
            job.RemoteId = copy.RemoteId;
            job.Attempts = 0;
            job.State = TransferState.Queued;
            job.NextAttempt = null;
            job.Failure = failure;
            job.FailureMessage = $"{explained} Carrying on from {to.Name}, which has the same file.";
            _jobs.Update(job);

            _logger.Info("Transfer {0} could not go on from {1} ({2}). Carrying on from {3}, which has the same file.", job.DownloadId, from, failure, to.Name);

            return true;
        }

        private void Fail(TransferJob job, TransferFailure failure, string detail)
        {
            DeleteFolder(_folders.IncompleteFolder(job));

            job.State = TransferState.Failed;
            job.Failure = failure;
            job.FailureMessage = Explain(job, failure, detail);
            job.NextAttempt = null;
            job.Ended = DateTime.UtcNow;
            _jobs.Update(job);
            _logger.Warn("Transfer {0} failed: {1}", job.DownloadId, job.FailureMessage);
        }

        // A transfer that failed for want of a direct route also says what is known about the
        // route. The same text reaches the Arr app's history.
        private string Explain(TransferJob job, TransferFailure failure, string detail)
        {
            if (failure is not (TransferFailure.NoDirectRoute or TransferFailure.DirectPathLost))
            {
                return TransferFailureText.For(failure, detail);
            }

            var peer = _peers.Find(job.PeerId);

            return TransferFailureText.For(failure, peer == null ? null : _routes.Describe(peer));
        }

        // Lets the connection to the other node be looked at again when an attempt says something
        // about it. See PeerConnectionUpkeep.
        private void AttemptEnded(TransferJob job, TransferFailure failure)
        {
            _eventAggregator.PublishEvent(new TransferAttemptEndedEvent(job.PeerId, failure));
        }

        private void Forget(TransferJob job)
        {
            try
            {
                _transport.Cancel(job.TransportId);
            }
            catch (TransportException ex)
            {
                _logger.Debug(ex, "Unable to stop transfer {0} in the transport", job.TransportId);
            }
        }

        private void DeleteFile(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn(ex, "Unable to remove {0}", file);
            }
        }

        private void DeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn(ex, "Unable to remove {0}", folder);
            }
        }

        private void Changed()
        {
            _eventAggregator.PublishEvent(new TransfersChangedEvent());
        }
    }
}

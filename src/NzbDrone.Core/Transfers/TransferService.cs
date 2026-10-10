using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerIndexer;

namespace NzbDrone.Core.Transfers
{
    public interface ITransferService
    {
        // Starts fetching what a ticket names. Returns null when no trusted node offers it any more.
        TransferJob Add(string candidateId, string category);

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
    public class TransferService : ITransferService, IHandle<PeerStateChangedEvent>, IHandle<PeerDeletedEvent>
    {
        public const int MaxConcurrent = 3;
        public const int MaxAttempts = 3;

        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

        private readonly ITransferRepository _jobs;
        private readonly ICandidateService _candidates;
        private readonly IPeerRepository _peers;
        private readonly IFileTransport _transport;
        private readonly ITransferFolders _folders;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly object _sync = new();

        public TransferService(ITransferRepository jobs,
                               ICandidateService candidates,
                               IPeerRepository peers,
                               IFileTransport transport,
                               ITransferFolders folders,
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            _jobs = jobs;
            _candidates = candidates;
            _peers = peers;
            _transport = transport;
            _folders = folders;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public TransferJob Add(string candidateId, string category)
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

                var job = _jobs.Insert(new TransferJob
                {
                    DownloadId = "SABnzbd_nzo_" + Guid.NewGuid().ToString("N")[..12],
                    CandidateId = candidateId,
                    PeerId = peer.Id,
                    PeerNodeId = peer.NodeId,
                    RemoteId = candidate.RemoteId,
                    MediaKind = candidate.MediaKind,
                    Title = candidate.ReleaseTitle,
                    Extension = candidate.Extension,
                    Category = category,
                    Size = candidate.Size,
                    State = TransferState.Queued,
                    Added = DateTime.UtcNow
                });

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

                var again = Add(failed.CandidateId, failed.Category);

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
                    changed |= Follow(job, progress.GetValueOrDefault(job.DownloadId));
                }

                var running = active.Count(j => j.State == TransferState.Downloading);

                foreach (var job in active.Where(j => j.State == TransferState.Queued).OrderBy(j => j.Added))
                {
                    if (running >= MaxConcurrent)
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

                    Fail(job, failure, null);
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
                    Id = job.DownloadId,
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
                _jobs.Update(job);

                return true;
            }

            var before = (job.BytesDone, job.Size, job.Hash);

            if (progress.Size > 0)
            {
                job.Size = progress.Size;
            }

            // While it is still connecting the transport has counted nothing yet. What the job
            // already holds stays on show until the transport says where it carried on from.
            if (progress.State != FileTransferState.Connecting)
            {
                job.BytesDone = progress.BytesDone;
            }

            job.Hash = progress.Hash ?? job.Hash;

            switch (progress.State)
            {
                case FileTransferState.Completed:
                    Forget(job);
                    Complete(job);

                    return true;

                case FileTransferState.Failed:
                    Forget(job);
                    Failed(job, TransferFailureText.FromTransport(progress.FailureKind), progress.FailureMessage);

                    return true;

                default:
                    if (before == (job.BytesDone, job.Size, job.Hash))
                    {
                        return false;
                    }

                    _jobs.SetFields(job, j => j.BytesDone, j => j.Size, j => j.Hash);

                    return true;
            }
        }

        // The file is verified. It appears under complete/ in one step, or not at all.
        private void Complete(TransferJob job)
        {
            var from = _folders.IncompleteFolder(job);
            var to = _folders.CompleteFolder(job);

            try
            {
                // Left behind by an earlier download of the same release.
                DeleteFolder(to);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                Directory.Move(from, to);
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

        private void Failed(TransferJob job, TransferFailure failure, string detail)
        {
            if (TransferFailureText.IsTransient(failure) && job.Attempts < MaxAttempts)
            {
                _logger.Info("Transfer {0} stopped ({1}: {2}). Trying again, attempt {3} of {4}", job.DownloadId, failure, detail, job.Attempts + 1, MaxAttempts);

                job.State = TransferState.Queued;
                job.NextAttempt = DateTime.UtcNow.Add(RetryDelay);
                job.Failure = failure;
                job.FailureMessage = TransferFailureText.For(failure, detail);
                _jobs.Update(job);

                return;
            }

            Fail(job, failure, detail);
        }

        private void Fail(TransferJob job, TransferFailure failure, string detail)
        {
            DeleteFolder(_folders.IncompleteFolder(job));

            job.State = TransferState.Failed;
            job.Failure = failure;
            job.FailureMessage = TransferFailureText.For(failure, detail);
            job.NextAttempt = null;
            job.Ended = DateTime.UtcNow;
            _jobs.Update(job);
            _logger.Warn("Transfer {0} failed: {1}", job.DownloadId, job.FailureMessage);
        }

        private void Forget(TransferJob job)
        {
            try
            {
                _transport.Cancel(job.DownloadId);
            }
            catch (TransportException ex)
            {
                _logger.Debug(ex, "Unable to stop transfer {0} in the transport", job.DownloadId);
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

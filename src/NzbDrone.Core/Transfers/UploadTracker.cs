using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Transfers
{
    public enum UploadState
    {
        // Allowed, and the file is being hashed before anything is sent.
        Preparing = 0,
        Sending = 1,
        Completed = 2,
        Failed = 3,

        // Allowed, and waiting for a free upload slot (Most Uploads at Once).
        Waiting = 4
    }

    // One file this node is sending, or has sent, to another node.
    public class Upload
    {
        public string Id { get; set; }
        public string PeerNodeId { get; set; }
        public string PeerName { get; set; }

        // The file's name on this machine. This is the owner's own view of their own file.
        public string Title { get; set; }
        public UploadState State { get; set; }
        public long Size { get; set; }
        public long BytesSent { get; set; }
        public int Attempts { get; set; }
        public string LastFailure { get; set; }
        public string LastFailureMessage { get; set; }

        // While waiting: its place in line, counting from 1.
        public int? QueuePosition { get; set; }

        // Why it stopped, in the transport's word for it. Null unless it failed.
        public string Failure { get; set; }
        public string FailureMessage { get; set; }
        public DateTime Started { get; set; }
        public DateTime? Ended { get; set; }

        public bool IsActive => State is UploadState.Waiting or UploadState.Preparing or UploadState.Sending;
    }

    // The other node said that a file this node sent did not match the hash it was sent with.
    public class UploadVerificationFailedEvent : IEvent
    {
        public UploadVerificationFailedEvent(string path)
        {
            Path = path;
        }

        // Where the file is on this machine.
        public string Path { get; }
    }

    public interface IUploadTracker
    {
        // What is being sent now, and what was sent since Nodearr started, newest first.
        List<Upload> All();

        // Brings the list up to date with the transport. Called once a second.
        void Tick();
    }

    // Follows what the transport is sending. Nothing here decides anything: whether a node may
    // have a file was settled before the upload began (FileSourceService). This is so the owner
    // can see their files leaving, which a download-only view never showed.
    //
    // The list is kept in memory. It starts empty each time Nodearr starts.
    public class UploadTracker : IUploadTracker
    {
        public const int MaxFinished = 100;

        private const string VerificationFailed = "VerificationFailed";

        private readonly IFileTransport _transport;
        private readonly IPeerRepository _peers;
        private readonly IEventAggregator _eventAggregator;
        private readonly Dictionary<string, Upload> _uploads = new();
        private readonly object _sync = new();

        public UploadTracker(IFileTransport transport, IPeerRepository peers, IEventAggregator eventAggregator)
        {
            _transport = transport;
            _peers = peers;
            _eventAggregator = eventAggregator;
        }

        public List<Upload> All()
        {
            lock (_sync)
            {
                return _uploads.Values
                    .OrderByDescending(u => u.IsActive)
                    .ThenByDescending(u => u.Started)
                    .Select(Copy)
                    .ToList();
            }
        }

        public void Tick()
        {
            List<FileUploadProgress> reported;

            try
            {
                reported = _transport.ListUploads();
            }
            catch (TransportException)
            {
                // Not running, so nothing is being sent. What was under way has stopped.
                reported = new List<FileUploadProgress>();
            }

            var changed = false;
            var mismatched = new List<string>();

            lock (_sync)
            {
                Dictionary<string, string> names = null;
                var seen = new HashSet<string>();

                foreach (var progress in reported)
                {
                    seen.Add(progress.Id);

                    if (!_uploads.TryGetValue(progress.Id, out var upload))
                    {
                        names ??= _peers.All().ToDictionary(p => p.NodeId, p => p.Name);

                        upload = new Upload
                        {
                            Id = progress.Id,
                            PeerNodeId = progress.RemoteNodeId,
                            PeerName = names.GetValueOrDefault(progress.RemoteNodeId ?? string.Empty),
                            Title = FileName(progress.Path),
                            Started = DateTime.UtcNow
                        };

                        _uploads[progress.Id] = upload;
                        changed = true;
                    }

                    var mismatchedBefore = upload.Failure == VerificationFailed;

                    changed |= Follow(upload, progress);

                    if (upload.Failure == VerificationFailed && !mismatchedBefore)
                    {
                        mismatched.Add(progress.Path);
                    }
                }

                // The transport restarted and forgot these. They did not finish.
                foreach (var upload in _uploads.Values.Where(u => u.IsActive && !seen.Contains(u.Id)))
                {
                    upload.State = UploadState.Failed;
                    upload.Failure = "Stopped";
                    upload.Ended = DateTime.UtcNow;
                    changed = true;
                }

                foreach (var old in _uploads.Values.Where(u => !u.IsActive).OrderByDescending(u => u.Ended).Skip(MaxFinished).ToList())
                {
                    _uploads.Remove(old.Id);
                }
            }

            foreach (var path in mismatched)
            {
                _eventAggregator.PublishEvent(new UploadVerificationFailedEvent(path));
            }

            if (changed)
            {
                _eventAggregator.PublishEvent(new TransfersChangedEvent());
            }
        }

        private static bool Follow(Upload upload, FileUploadProgress progress)
        {
            var before = (upload.State, upload.BytesSent, upload.Size, upload.Failure, upload.Attempts, upload.LastFailure, upload.LastFailureMessage, upload.QueuePosition);

            upload.State = progress.State;
            upload.Size = progress.Size;
            upload.BytesSent = progress.BytesSent;
            upload.Failure = progress.FailureKind;
            upload.FailureMessage = progress.FailureMessage;
            upload.Attempts = Math.Max(progress.Attempts, 1);
            upload.LastFailure = progress.LastFailureKind;
            upload.LastFailureMessage = progress.LastFailureMessage;
            upload.QueuePosition = progress.State == UploadState.Waiting ? progress.QueuePosition : null;

            if (upload.IsActive)
            {
                upload.Ended = null;
            }
            else
            {
                upload.Ended ??= DateTime.UtcNow;
            }

            return before != (upload.State, upload.BytesSent, upload.Size, upload.Failure, upload.Attempts, upload.LastFailure, upload.LastFailureMessage, upload.QueuePosition);
        }

        // The transport may run on another system's idea of a path, so both separators count.
        private static string FileName(string path)
        {
            return (path ?? string.Empty).Split('/', '\\').LastOrDefault(part => part.Length > 0) ?? string.Empty;
        }

        // A copy, so that a caller reading the list is not handed objects still being updated.
        private static Upload Copy(Upload upload)
        {
            return new Upload
            {
                Id = upload.Id,
                PeerNodeId = upload.PeerNodeId,
                PeerName = upload.PeerName,
                Title = upload.Title,
                State = upload.State,
                Size = upload.Size,
                BytesSent = upload.BytesSent,
                Attempts = upload.Attempts,
                LastFailure = upload.LastFailure,
                LastFailureMessage = upload.LastFailureMessage,
                QueuePosition = upload.QueuePosition,
                Failure = upload.Failure,
                FailureMessage = upload.FailureMessage,
                Started = upload.Started,
                Ended = upload.Ended
            };
        }
    }
}

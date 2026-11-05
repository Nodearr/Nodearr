using System;
using System.Linq;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Statistics
{
    // Adds to the hourly statistics as things happen: a download that ended for good, each
    // upload as the tracker follows it, and each connection check. Nothing is read back from
    // transfer rows or the transport later, so the history outlives both.
    public class StatisticsRecorder : IHandle<TransferEndedEvent>,
                                      IHandle<UploadRequestedEvent>,
                                      IHandle<UploadWaitEndedEvent>,
                                      IHandle<UploadAttemptEndedEvent>,
                                      IHandle<UploadReopenedEvent>,
                                      IHandle<PeerConnectionCheckedEvent>
    {
        // What a finished transfer's row says for its failure.
        public const string Finished = "";

        // What the tracker says of an upload whose reason the transport did not give.
        private const string UploadFailed = "Failed";

        private static readonly object Sync = new();

        private readonly ITransferStatisticRepository _transfers;
        private readonly IConnectionStatisticRepository _connections;
        private readonly IPeerRouteRepository _routes;
        private readonly IFetchedTitleRepository _fetchedTitles;
        private readonly IStatisticsTitles _titles;
        private readonly IApplicationFactory _applications;

        public StatisticsRecorder(ITransferStatisticRepository transfers,
                                  IConnectionStatisticRepository connections,
                                  IPeerRouteRepository routes,
                                  IFetchedTitleRepository fetchedTitles,
                                  IStatisticsTitles titles,
                                  IApplicationFactory applications)
        {
            _transfers = transfers;
            _connections = connections;
            _routes = routes;
            _fetchedTitles = fetchedTitles;
            _titles = titles;
            _applications = applications;
        }

        // What a download that ended for good adds, and where. The migration that made these
        // tables works out the same for the transfers already there (add_statistics).
        public static (DateTime Hour, string Failure, long Bytes, double Seconds) ForDownload(TransferJob job)
        {
            var ended = job.Ended ?? DateTime.UtcNow;
            var failure = job.State == TransferState.Completed ? Finished : job.Failure.ToString();
            var bytes = job.BytesMoved;
            var seconds = job.MovingMilliseconds / 1000.0;

            // Ended before Nodearr counted what moved, or with a transport too old to say. What
            // the job holds, and the time from its first attempt, are the best there is.
            if (job.BytesMoved == 0 && job.MovingMilliseconds == 0)
            {
                bytes = job.State == TransferState.Completed ? job.Size : job.BytesDone;
                seconds = job.Started.HasValue ? Math.Max(0, (ended - job.Started.Value).TotalSeconds) : 0;
            }

            return (StatisticsHours.HourOf(ended), failure, bytes, seconds);
        }

        public void Handle(TransferEndedEvent message)
        {
            var job = message.Job;
            var (hour, failure, bytes, seconds) = ForDownload(job);

            Add(hour, job.PeerNodeId, ApplicationFor(job.Category), TransferDirection.Fetched, failure, s =>
            {
                s.Bytes += bytes;
                s.Transfers++;
                s.Seconds += seconds;

                if (string.IsNullOrEmpty(failure) && seconds > 0)
                {
                    s.PeakBytesPerSecond = Math.Max(s.PeakBytesPerSecond, bytes / seconds);
                }
            });
        }

        public void Handle(UploadRequestedEvent message)
        {
            var upload = message.Upload;

            Add(StatisticsHours.HourOf(upload.Started), upload.PeerNodeId, ApplicationOf(upload), TransferDirection.Sent, Finished, s => s.Requests++);
        }

        public void Handle(UploadWaitEndedEvent message)
        {
            var upload = message.Upload;
            var waited = Math.Max(0, message.Waited.TotalSeconds);

            Add(StatisticsHours.HourOf(DateTime.UtcNow), upload.PeerNodeId, ApplicationOf(upload), TransferDirection.Sent, Finished, s =>
            {
                s.Waited++;
                s.WaitSeconds += waited;
                s.LongestWaitSeconds = Math.Max(s.LongestWaitSeconds, waited);
            });
        }

        // An upload that failed is counted as failed at once. If the other node asks for it
        // again, that is taken back (UploadReopenedEvent), so one file is one transfer however
        // many attempts it took, as it is on the fetching side. The take-back is kept beside
        // the count rather than lowering it, so the count only goes up.
        public void Handle(UploadAttemptEndedEvent message)
        {
            var upload = message.Upload;
            var title = _titles.TitleOf(upload.File);
            var applicationId = ApplicationOf(title);
            var hour = StatisticsHours.HourOf(upload.Ended ?? DateTime.UtcNow);
            var finished = upload.State == UploadState.Completed;
            var failure = finished ? Finished : upload.Failure ?? UploadFailed;
            var seconds = Math.Max(0, message.Moving.TotalSeconds);

            Add(hour, upload.PeerNodeId, applicationId, TransferDirection.Sent, failure, s =>
            {
                s.Bytes += message.Bytes;
                s.Transfers++;
                s.Seconds += seconds;

                if (finished && seconds > 0)
                {
                    s.PeakBytesPerSecond = Math.Max(s.PeakBytesPerSecond, message.Bytes / seconds);
                }
            });

            if (finished && title.HasValue)
            {
                lock (Sync)
                {
                    if (!_fetchedTitles.Exists(hour, applicationId, title.Value.Title))
                    {
                        _fetchedTitles.Insert(new FetchedTitle { Hour = hour, ApplicationId = applicationId, Title = title.Value.Title });
                    }
                }
            }
        }

        public void Handle(UploadReopenedEvent message)
        {
            var upload = message.Upload;

            Add(StatisticsHours.HourOf(message.PreviousEnded), upload.PeerNodeId, ApplicationOf(upload), TransferDirection.Sent, message.PreviousFailure, s => s.Reopened++);
        }

        public void Handle(PeerConnectionCheckedEvent message)
        {
            var peer = message.Peer;
            var path = peer.ConnectionPath;
            var at = peer.ConnectionChecked ?? DateTime.UtcNow;

            lock (Sync)
            {
                var changed = false;

                if (path != ConnectionPath.Unknown)
                {
                    var route = _routes.FindByNodeId(peer.NodeId);

                    if (route == null)
                    {
                        _routes.Insert(new PeerRoute { PeerNodeId = peer.NodeId, Path = path });
                    }
                    else if (route.Path != path)
                    {
                        route.Path = path;
                        route.Changed = at;
                        _routes.Update(route);
                        changed = true;
                    }
                }

                var hour = StatisticsHours.HourOf(at);
                var row = _connections.Find(hour, peer.NodeId) ?? new ConnectionStatistic { Hour = hour, PeerNodeId = peer.NodeId };

                switch (path)
                {
                    case ConnectionPath.Direct:
                        row.Direct++;
                        break;
                    case ConnectionPath.Relay:
                        row.RelayOnly++;
                        break;
                    default:
                        row.None++;
                        break;
                }

                if (changed)
                {
                    row.RouteChanges++;
                }

                _connections.Upsert(row);
            }
        }

        // The Arr app whose download category this is. Two apps could share one; the first
        // added is taken, as the Arr apps take the first matching download client.
        private int ApplicationFor(string category)
        {
            return _applications.All()
                .OrderBy(a => a.Id)
                .FirstOrDefault(a => category != null && string.Equals((a.Settings as ArrApplicationSettings)?.DownloadCategory, category, StringComparison.OrdinalIgnoreCase))
                ?.Id ?? 0;
        }

        private int ApplicationOf(Upload upload)
        {
            return ApplicationOf(_titles.TitleOf(upload.File));
        }

        // A file that is not in the catalog, or whose app has since been removed, is counted under
        // no app.
        private int ApplicationOf((int ApplicationId, string Title)? title)
        {
            return title.HasValue && _applications.All().Any(a => a.Id == title.Value.ApplicationId) ? title.Value.ApplicationId : 0;
        }

        private void Add(DateTime hour, string peerNodeId, int applicationId, TransferDirection direction, string failure, Action<TransferStatistic> change)
        {
            peerNodeId ??= string.Empty;

            lock (Sync)
            {
                var row = _transfers.Find(hour, peerNodeId, applicationId, direction, failure) ?? new TransferStatistic
                {
                    Hour = hour,
                    PeerNodeId = peerNodeId,
                    ApplicationId = applicationId,
                    Direction = direction,
                    Failure = failure
                };

                change(row);
                _transfers.Upsert(row);
            }
        }
    }
}

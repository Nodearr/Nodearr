using System.Linq;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Update
{
    public interface IUpdateQuietMoment
    {
        // What an automatic update would interrupt now, or null when nothing would.
        string Busy();
    }

    // An automatic update waits until no transfer is queued or under way, nothing is being sent to a
    // friend, and no catalog is being read or exchanged (ADR 0024).
    public class UpdateQuietMoment : IUpdateQuietMoment
    {
        private readonly ITransferService _transfers;
        private readonly IUploadTracker _uploads;
        private readonly IManageCommandQueue _commandQueue;

        public UpdateQuietMoment(ITransferService transfers, IUploadTracker uploads, IManageCommandQueue commandQueue)
        {
            _transfers = transfers;
            _uploads = uploads;
            _commandQueue = commandQueue;
        }

        public string Busy()
        {
            var downloads = _transfers.All().Count(t => t.State is TransferState.Queued or TransferState.Downloading);

            if (downloads > 0)
            {
                return $"{downloads} transfer(s) queued or under way";
            }

            var uploads = _uploads.All().Count(u => u.IsActive);

            if (uploads > 0)
            {
                return $"{uploads} upload(s) under way";
            }

            var catalogWork = _commandQueue.All().Any(c =>
                c.Status is CommandStatus.Queued or CommandStatus.Started &&
                c.Body is CatalogRefreshCommand or PeerCatalogSyncCommand);

            return catalogWork ? "a catalog refresh or exchange under way" : null;
        }
    }
}

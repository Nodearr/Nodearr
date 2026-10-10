using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Transfers
{
    public interface ITransferRepository : IBasicRepository<TransferJob>
    {
        TransferJob FindByDownloadId(string downloadId);
        List<TransferJob> Active();
    }

    public class TransferRepository : BasicRepository<TransferJob>, ITransferRepository
    {
        public TransferRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public TransferJob FindByDownloadId(string downloadId)
        {
            return Query(j => j.DownloadId == downloadId).FirstOrDefault();
        }

        public List<TransferJob> Active()
        {
            return Query(j => j.State == TransferState.Queued || j.State == TransferState.Downloading);
        }
    }
}

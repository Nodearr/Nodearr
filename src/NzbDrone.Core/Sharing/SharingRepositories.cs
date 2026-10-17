using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Sharing
{
    public interface IShareListRepository : IBasicRepository<ShareList>
    {
    }

    public class ShareListRepository : BasicRepository<ShareList>, IShareListRepository
    {
        public ShareListRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface IShareListItemRepository : IBasicRepository<ShareListItem>
    {
        List<ShareListItem> ForList(int shareListId);
        void DeleteForList(int shareListId);
        void DeleteForApplication(int applicationId);
    }

    public class ShareListItemRepository : BasicRepository<ShareListItem>, IShareListItemRepository
    {
        public ShareListItemRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<ShareListItem> ForList(int shareListId)
        {
            return Query(i => i.ShareListId == shareListId);
        }

        public void DeleteForList(int shareListId)
        {
            Delete(i => i.ShareListId == shareListId);
        }

        public void DeleteForApplication(int applicationId)
        {
            Delete(i => i.ApplicationId == applicationId);
        }
    }

    public interface IOfferRecordRepository : IBasicRepository<OfferRecord>
    {
        OfferRecord FindForPeer(int peerId);
        void Save(OfferRecord record);
        void DeleteForPeer(int peerId);
    }

    public class OfferRecordRepository : BasicRepository<OfferRecord>, IOfferRecordRepository
    {
        public OfferRecordRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public OfferRecord FindForPeer(int peerId)
        {
            return Query(r => r.PeerId == peerId).SingleOrDefault();
        }

        // One record for each peer: the latest replaces the one before.
        public void Save(OfferRecord record)
        {
            var existing = FindForPeer(record.PeerId);

            if (existing != null)
            {
                record.Id = existing.Id;
                Update(record);
            }
            else
            {
                Insert(record);
            }
        }

        public void DeleteForPeer(int peerId)
        {
            Delete(r => r.PeerId == peerId);
        }
    }

    public interface IShareListPeerRepository : IBasicRepository<ShareListPeer>
    {
        List<ShareListPeer> ForList(int shareListId);
        void DeleteForList(int shareListId);
        void DeleteForPeer(int peerId);
    }

    public class ShareListPeerRepository : BasicRepository<ShareListPeer>, IShareListPeerRepository
    {
        public ShareListPeerRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<ShareListPeer> ForList(int shareListId)
        {
            return Query(p => p.ShareListId == shareListId);
        }

        public void DeleteForList(int shareListId)
        {
            Delete(p => p.ShareListId == shareListId);
        }

        public void DeleteForPeer(int peerId)
        {
            Delete(p => p.PeerId == peerId);
        }
    }

    public interface ISentTitleRepository : IBasicRepository<SentTitle>
    {
        List<SentTitle> WaitingForInvite(int inviteId);
        List<SentTitle> Undelivered(int peerId);
        void DeleteForPeer(int peerId);
    }

    public class SentTitleRepository : BasicRepository<SentTitle>, ISentTitleRepository
    {
        public SentTitleRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<SentTitle> WaitingForInvite(int inviteId)
        {
            return Query(s => s.InviteId == inviteId && s.PeerId == null);
        }

        public List<SentTitle> Undelivered(int peerId)
        {
            return Query(s => s.PeerId == peerId && s.DeliveredAt == null);
        }

        public void DeleteForPeer(int peerId)
        {
            Delete(s => s.PeerId == peerId);
        }
    }

    public interface IReceivedTitleRepository : IBasicRepository<ReceivedTitle>
    {
        ReceivedTitle Find(int peerId, string sendId);
        int CountWaiting(int peerId);
        void DeleteForPeer(int peerId);
    }

    public class ReceivedTitleRepository : BasicRepository<ReceivedTitle>, IReceivedTitleRepository
    {
        public ReceivedTitleRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public ReceivedTitle Find(int peerId, string sendId)
        {
            return Query(r => r.PeerId == peerId && r.SendId == sendId).SingleOrDefault();
        }

        public int CountWaiting(int peerId)
        {
            return Query(r => r.PeerId == peerId && r.Handled == null).Count;
        }

        public void DeleteForPeer(int peerId)
        {
            Delete(r => r.PeerId == peerId);
        }
    }
}

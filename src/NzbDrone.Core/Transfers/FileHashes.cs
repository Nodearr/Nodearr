using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Transfers
{
    // The hash of a file on this machine, as the transport worked it out, and the version of the
    // file it belongs to.
    public class FileHash : ModelBase
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public long ModifiedNs { get; set; }
        public string Blake3 { get; set; }
        public DateTime Hashed { get; set; }

        // The offered file this is, as offers name it, so an offer can carry the hash.
        public string OfferFileId { get; set; }

        public HashedFile ToHashedFile()
        {
            return new HashedFile { Size = Size, ModifiedNs = ModifiedNs, Blake3 = Blake3 };
        }

        public bool Matches(HashedFile hashed)
        {
            return hashed != null && hashed.Size == Size && hashed.ModifiedNs == ModifiedNs && hashed.Blake3 == Blake3;
        }
    }

    public interface IFileHashRepository : IBasicRepository<FileHash>
    {
        FileHash FindByPath(string path);

        // Each kept hash by the offered file it is for.
        Dictionary<string, FileHash> ByOfferFileId();
    }

    public class FileHashRepository : BasicRepository<FileHash>, IFileHashRepository
    {
        public FileHashRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public FileHash FindByPath(string path)
        {
            return Query(h => h.Path == path).SingleOrDefault();
        }

        public Dictionary<string, FileHash> ByOfferFileId()
        {
            return All().Where(h => h.OfferFileId != null).GroupBy(h => h.OfferFileId).ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Hashed).First());
        }
    }
}

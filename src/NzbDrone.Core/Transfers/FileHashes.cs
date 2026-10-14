using System;
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
    }
}

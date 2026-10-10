using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Transfers
{
    public interface IFileSourceService
    {
        FileGrant Resolve(string remoteNodeId, string file);
    }

    // The sending side of a transfer. Every request for a file is judged here, at the moment it
    // arrives, against who the node is now and what it is offered now. Having been offered a file
    // earlier, or holding an old name for it, earns nothing.
    public class FileSourceService : IFileSourceService, IHandle<ApplicationStartedEvent>
    {
        public const string NotAuthorized = "NotAuthorized";
        public const string Revoked = "Revoked";
        public const string NotFound = "NotFound";

        private readonly IPeerRepository _peers;
        private readonly ISharingService _sharing;
        private readonly ICatalogFileLocator _locator;
        private readonly IFileTransport _transport;
        private readonly Logger _logger;

        public FileSourceService(IPeerRepository peers, ISharingService sharing, ICatalogFileLocator locator, IFileTransport transport, Logger logger)
        {
            _peers = peers;
            _sharing = sharing;
            _locator = locator;
            _transport = transport;
            _logger = logger;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _transport.SetSource(Resolve);
        }

        public FileGrant Resolve(string remoteNodeId, string file)
        {
            var peer = _peers.FindByNodeId(remoteNodeId);

            if (peer is not { State: PeerState.Trusted })
            {
                _logger.Warn("Refused a file request from node {0}, which is not trusted", remoteNodeId);

                return FileGrant.Refuse(peer is { State: PeerState.Revoked or PeerState.Blocked } ? Revoked : NotAuthorized);
            }

            // The same rule that builds the offer, asked again now.
            if (!_sharing.IsOffered(peer, file))
            {
                _logger.Warn("Refused {0} a file it is not offered: {1}", peer.Name, file);

                return FileGrant.Refuse(NotAuthorized);
            }

            var located = _locator.Locate(file);

            if (located is not { Readable: true })
            {
                _logger.Warn("{0} asked for {1}, which cannot be read here ({2}). Check the path mappings.", peer.Name, file, located?.LocalPath ?? "not in the catalog");

                return FileGrant.Refuse(NotFound);
            }

            _logger.Info("Sending {0} to {1}", located.LocalPath, peer.Name);

            return FileGrant.Allow(located.LocalPath);
        }
    }
}

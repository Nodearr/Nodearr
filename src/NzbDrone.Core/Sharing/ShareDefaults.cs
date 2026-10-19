using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Sharing
{
    public interface IShareDefaults
    {
        // The Arr apps a new friend offered everything is not offered from: those whose Share With
        // New Friends is off, as Whisparr's is unless turned on.
        List<int> NotSharedWithNewFriends();
    }

    // An app added later is left out for every friend there already when it is not shared with new
    // friends. An app removed is taken off every friend's list, so that a new app given its number is
    // not left out by mistake.
    public class ShareDefaults : IShareDefaults,
                                 IHandle<ProviderAddedEvent<IApplication>>,
                                 IHandle<ProviderDeletedEvent<IApplication>>
    {
        private readonly IApplicationFactory _applications;
        private readonly IPeerRepository _peers;
        private readonly IEventAggregator _eventAggregator;

        public ShareDefaults(IApplicationFactory applications, IPeerRepository peers, IEventAggregator eventAggregator)
        {
            _applications = applications;
            _peers = peers;
            _eventAggregator = eventAggregator;
        }

        public List<int> NotSharedWithNewFriends()
        {
            return _applications.All().Where(a => !SharedWithNewFriends(a)).Select(a => a.Id).ToList();
        }

        public void Handle(ProviderAddedEvent<IApplication> message)
        {
            var id = message.Definition.Id;

            if (!SharedWithNewFriends(message.Definition))
            {
                Change(p => p.ExcludedApplicationIds.Contains(id) ? null : p.ExcludedApplicationIds.Append(id).ToList());
            }
        }

        public void Handle(ProviderDeletedEvent<IApplication> message)
        {
            var id = message.ProviderId;

            Change(p => p.ExcludedApplicationIds.Contains(id) ? p.ExcludedApplicationIds.Where(i => i != id).ToList() : null);
        }

        private static bool SharedWithNewFriends(ProviderDefinition definition)
        {
            return definition.Settings is not ArrApplicationSettings settings || settings.ShareWithNewFriends;
        }

        // The new list for a peer, or null when it stays as it is.
        private void Change(Func<Peer, List<int>> change)
        {
            var changed = false;

            foreach (var peer in _peers.All())
            {
                var ids = change(peer);

                if (ids != null)
                {
                    peer.ExcludedApplicationIds = ids;
                    _peers.SetFields(peer, p => p.ExcludedApplicationIds);
                    changed = true;
                }
            }

            if (changed)
            {
                _eventAggregator.PublishEvent(new SharingChangedEvent());
            }
        }
    }
}

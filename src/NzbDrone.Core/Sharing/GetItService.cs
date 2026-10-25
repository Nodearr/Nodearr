using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.PeerCatalog;

namespace NzbDrone.Core.Sharing
{
    public class GetItException : NzbDroneException
    {
        public GetItException(string message)
            : base(message)
        {
        }
    }

    // How to add what was sent, where the Arr app does not have it yet.
    public class GetItOptions
    {
        public int ApplicationId { get; set; }
        public string RootFolderPath { get; set; }
        public int QualityProfileId { get; set; }
        public int? MetadataProfileId { get; set; }
        public string SeriesType { get; set; }
        public bool SeasonFolder { get; set; } = true;
    }

    // One local Arr app what was sent could go to.
    public class GetItTarget
    {
        public int ApplicationId { get; set; }
        public string ApplicationName { get; set; }
        public string Implementation { get; set; }
        public bool InLibrary { get; set; }
        public LibraryAddChoices Choices { get; set; }
        public GetItOptions Defaults { get; set; }

        // Why it cannot be used now, such as the app not answering.
        public string Problem { get; set; }
    }

    public class GetItPlan
    {
        public List<GetItTarget> Targets { get; set; } = new();

        // Why nothing can be added, when that is so.
        public string Problem { get; set; }
    }

    public class GetItCommand : Command
    {
        public int ReceivedTitleId { get; set; }
        public int ApplicationId { get; set; }
        public string RootFolderPath { get; set; }
        public int QualityProfileId { get; set; }
        public int? MetadataProfileId { get; set; }
        public string SeriesType { get; set; }
        public bool SeasonFolder { get; set; } = true;

        public override bool SendUpdatesToClient => true;
        public override bool UpdateScheduledTask => false;
    }

    public interface IGetItService
    {
        GetItPlan Plan(int receivedTitleId);
        void GetIt(int receivedTitleId, GetItOptions options);
    }

    // Keeps the state of a received send; the library operation is shared with the peer catalog.
    public class GetItService : IGetItService, IExecute<GetItCommand>, IHandle<ApplicationStartedEvent>
    {
        private readonly IReceivedTitleRepository _received;
        private readonly IPeerRepository _peers;
        private readonly IPeerCatalogRepository _peerItems;
        private readonly ILibraryGetItService _library;
        private readonly IManageCommandQueue _commandQueue;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public GetItService(IReceivedTitleRepository received,
                            IPeerRepository peers,
                            IPeerCatalogRepository peerItems,
                            ILibraryGetItService library,
                            IManageCommandQueue commandQueue,
                            IEventAggregator eventAggregator,
                            Logger logger)
        {
            _received = received;
            _peers = peers;
            _peerItems = peerItems;
            _library = library;
            _commandQueue = commandQueue;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public GetItPlan Plan(int receivedTitleId)
        {
            return _library.Plan(TitleFor(_received.Get(receivedTitleId)));
        }

        public void GetIt(int receivedTitleId, GetItOptions options)
        {
            var received = _received.Get(receivedTitleId);

            if (received.GetItState == GetItState.Working)
            {
                throw new GetItException("This is being added already");
            }

            var peer = _peers.Find(received.PeerId);

            if (peer is not { State: PeerState.Trusted } || received.Handled.HasValue)
            {
                throw new GetItException("This is no longer waiting");
            }

            var definition = _library.CheckOptions(TitleFor(received), options);

            SetState(received, GetItState.Working, $"Asking {definition.Name} to add it");

            _commandQueue.Push(new GetItCommand
            {
                ReceivedTitleId = received.Id,
                ApplicationId = definition.Id,
                RootFolderPath = options.RootFolderPath,
                QualityProfileId = options.QualityProfileId,
                MetadataProfileId = options.MetadataProfileId,
                SeriesType = options.SeriesType,
                SeasonFolder = options.SeasonFolder
            });
        }

        public void Execute(GetItCommand message)
        {
            var received = _received.Find(message.ReceivedTitleId);

            if (received == null)
            {
                return;
            }

            try
            {
                var done = _library.AddAndSearch(TitleFor(received), new GetItOptions
                {
                    ApplicationId = message.ApplicationId,
                    RootFolderPath = message.RootFolderPath,
                    QualityProfileId = message.QualityProfileId,
                    MetadataProfileId = message.MetadataProfileId,
                    SeriesType = message.SeriesType,
                    SeasonFolder = message.SeasonFolder
                });

                _logger.Info("{0}: {1}", received.Title, done);
                SetState(received, GetItState.Done, done);
            }
            catch (Exception ex) when (ex is LibraryAddException or GetItException)
            {
                _logger.Warn(ex, "Unable to add {0}", received.Title);
                SetState(received, GetItState.Failed, ex.Message);
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex, "Unable to add {0}", received.Title);
                SetState(received, GetItState.Failed, $"The app answered {(int)ex.Response.StatusCode}: {Summary(ex.Response.Content)}");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to add {0}", received.Title);
                SetState(received, GetItState.Failed, ex.Message);
            }
        }

        // One that was being added when Nodearr stopped will not finish now.
        public void Handle(ApplicationStartedEvent message)
        {
            foreach (var received in _received.All().Where(r => r.GetItState == GetItState.Working))
            {
                SetState(received, GetItState.Failed, "Nodearr stopped before it finished. Try again");
            }
        }

        private GetItTitle TitleFor(ReceivedTitle received)
        {
            return GetItTitle.FromReceived(received, _peerItems.ForPeer(received.PeerId));
        }

        private void SetState(ReceivedTitle received, GetItState state, string message)
        {
            received.GetItState = state;
            received.GetItMessage = message;
            received.GetItCompletedAt = state == GetItState.Done ? DateTime.UtcNow : null;
            _received.SetFields(received, r => r.GetItState, r => r.GetItMessage, r => r.GetItCompletedAt);
            _eventAggregator.PublishEvent(new ReceivedTitlesChangedEvent());
        }

        // An Arr app's error answer, short enough to show.
        private static string Summary(string content)
        {
            var text = (content ?? string.Empty).Trim();

            return text.Length > 300 ? text[..300] : text;
        }
    }
}

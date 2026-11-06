using System;
using System.Threading;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Update.Commands;

namespace NzbDrone.Core.Update
{
    // While an automatic update waits for a quiet moment, asks again every 5 minutes whether it can
    // go. The installer decides, and after 24 hours of waiting it goes anyway (ADR 0024).
    public class UpdateWaiter : IHandle<ApplicationStartedEvent>, IHandle<ApplicationShutdownRequested>
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        private readonly ICheckUpdateService _checkUpdateService;
        private readonly IPendingUpdate _pendingUpdate;
        private readonly IManageCommandQueue _commandQueue;
        private Timer _timer;

        public UpdateWaiter(ICheckUpdateService checkUpdateService, IPendingUpdate pendingUpdate, IManageCommandQueue commandQueue)
        {
            _checkUpdateService = checkUpdateService;
            _pendingUpdate = pendingUpdate;
            _commandQueue = commandQueue;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _timer = new Timer(_ => Tick(), null, Interval, Interval);
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Tick()
        {
            var available = _checkUpdateService.AvailableUpdate();

            if (available != null && _pendingUpdate.WaitingSince(available.SemanticVersion) != null)
            {
                _commandQueue.Push(new ApplicationUpdateCommand(), trigger: CommandTrigger.Scheduled);
            }
        }
    }
}

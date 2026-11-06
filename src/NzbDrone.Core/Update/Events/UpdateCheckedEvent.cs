using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Update.Events
{
    // A check for updates ended, whatever it found.
    public class UpdateCheckedEvent : IEvent
    {
        public UpdateCheckedEvent(UpdateCheckStatus status)
        {
            Status = status;
        }

        public UpdateCheckStatus Status { get; }
    }
}

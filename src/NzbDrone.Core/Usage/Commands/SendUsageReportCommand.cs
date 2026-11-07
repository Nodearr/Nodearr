using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Usage.Commands
{
    // Every 6 hours, as often as Sonarr and Radarr send theirs (ADR 0024). Shown on System > Tasks
    // as Send Usage Report.
    public class SendUsageReportCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override string CompletionMessage => null;
    }
}

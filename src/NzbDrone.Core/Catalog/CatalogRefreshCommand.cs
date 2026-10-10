using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Catalog
{
    public class CatalogRefreshCommand : Command
    {
        // Refreshes every enabled application when not set.
        public int? ApplicationId { get; set; }

        // Removes items already marked missing right away instead of waiting out the retention period.
        public bool PurgeMissing { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool IsLongRunning => true;

        // A refresh of one application must not reset the timer for the scheduled refresh of all of them.
        public override bool UpdateScheduledTask => !ApplicationId.HasValue;
        public override string CompletionMessage => "Completed";
    }
}

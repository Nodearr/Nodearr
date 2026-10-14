using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Catalog
{
    // Reads one series or movie from one Arr app again, after the Arr app said it changed. Two of
    // these for the same title, queued while the first waits, are one: a season imported file by
    // file is read once.
    public class CatalogTitleRefreshCommand : Command
    {
        public int ApplicationId { get; set; }

        // The series or the movie, as the Arr app numbers it.
        public int ArrId { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool UpdateScheduledTask => false;
        public override string CompletionMessage => null;
    }
}

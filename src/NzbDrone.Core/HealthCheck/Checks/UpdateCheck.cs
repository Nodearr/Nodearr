using System;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // A manifest that is not signed by Nodearr's release key is a warning: someone may be pointing
    // this node at packages of their choosing. An update that has been waiting 14 days is a notice
    // (ADR 0024). A manifest that cannot be fetched says nothing.
    [CheckOn(typeof(UpdateCheckedEvent))]
    public class UpdateCheck : HealthCheckBase
    {
        public static readonly TimeSpan NoticeAfter = TimeSpan.FromDays(14);

        private readonly ICheckUpdateService _checkUpdateService;

        public UpdateCheck(ICheckUpdateService checkUpdateService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _checkUpdateService = checkUpdateService;
        }

        // For testing: the time now.
        public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        public override HealthCheck Check()
        {
            var status = _checkUpdateService.Status();

            if (status.Outcome == UpdateCheckOutcome.BadSignature)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.UpdateSignatureInvalid,
                    _localizationService.GetLocalizedString("UpdateCheckSignatureMessage"));
            }

            if (status.Available != null && status.AvailableSince.HasValue && Now() - status.AvailableSince.Value >= NoticeAfter)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Notice,
                    HealthCheckReason.UpdateAvailable,
                    _localizationService.GetLocalizedString("UpdateCheckAvailableMessage", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "version", status.Available.SemanticVersion.ToString() },
                        { "days", (int)(Now() - status.AvailableSince.Value).TotalDays }
                    }));
            }

            return new HealthCheck(GetType());
        }

        // The first check runs a moment after start, and says when it ends.
        public override bool CheckOnStartup => false;
    }
}

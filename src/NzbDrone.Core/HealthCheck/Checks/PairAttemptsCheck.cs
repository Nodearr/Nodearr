using System;
using System.Collections.Generic;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Nodes;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Someone who is not a friend keeps trying to pair. Nothing can be guessed, but the person who
    // runs the node should know, and should know whom they gave invites to.
    [CheckOn(typeof(PairAttemptsLimitedEvent))]
    public class PairAttemptsCheck : HealthCheckBase
    {
        private readonly IPairAttemptLimiter _pairAttempts;

        public PairAttemptsCheck(IPairAttemptLimiter pairAttempts, ILocalizationService localizationService)
            : base(localizationService)
        {
            _pairAttempts = pairAttempts;
        }

        public override HealthCheck Check()
        {
            var now = DateTime.UtcNow;
            var summary = _pairAttempts.Summary(now);

            if (summary.LimitedNodes == 0 && summary.Failures < PairAttemptLimiter.WarnAtFailures)
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Warning,
                HealthCheckReason.PairAttempts,
                _localizationService.GetLocalizedString("PairAttemptsCheckMessage", new Dictionary<string, object>
                {
                    { "failures", summary.Failures },
                    { "nodes", summary.Nodes },
                    { "time", now.ToLocalTime().ToString("t") }
                }));
        }

        // Nothing has been tried at startup. A restart forgets the attempts.
        public override bool CheckOnStartup => false;
    }
}

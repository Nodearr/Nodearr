using NzbDrone.Core.Localization;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(PeerTransportStateChangedEvent))]
    public class PeerTransportCheck : HealthCheckBase
    {
        private readonly IPeerTransport _transport;

        public PeerTransportCheck(IPeerTransport transport, ILocalizationService localizationService)
            : base(localizationService)
        {
            _transport = transport;
        }

        public override HealthCheck Check()
        {
            if (_transport.IsRunning)
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Error,
                HealthCheckReason.PeerTransportNotRunning,
                _localizationService.GetLocalizedString("PeerTransportCheckMessage"));
        }

        // The transport starts a moment after the application does and announces it with an event.
        public override bool CheckOnStartup => false;
    }
}

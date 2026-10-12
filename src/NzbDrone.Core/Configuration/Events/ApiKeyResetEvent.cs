using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Configuration.Events
{
    // Raised when Nodearr's API key has been replaced. Sonarr and Radarr hold the key, for the
    // indexer and the download client Nodearr added to them, and have to be given the new one.
    public class ApiKeyResetEvent : IEvent
    {
    }
}

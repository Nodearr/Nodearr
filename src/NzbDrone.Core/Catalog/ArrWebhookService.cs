using System.Text.Json;
using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Catalog
{
    public interface IArrWebhookService
    {
        // What an Arr app's webhook sent. False when the body is not one Nodearr understands.
        bool Receive(int applicationId, string body);
    }

    // Takes the Arr apps' own webhook. Its body names the series, movie or artist the event was about, and
    // that is all Nodearr reads: the title is then read again from the Arr app itself, so the
    // catalog holds what the Arr app says now, whatever the event was.
    public class ArrWebhookService : IArrWebhookService
    {
        private readonly IManageCommandQueue _commandQueue;
        private readonly Logger _logger;

        public ArrWebhookService(IManageCommandQueue commandQueue, Logger logger)
        {
            _commandQueue = commandQueue;
            _logger = logger;
        }

        public bool Receive(int applicationId, string body)
        {
            JsonElement root;

            try
            {
                using var document = JsonDocument.Parse(body ?? string.Empty);

                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return false;
            }

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("eventType", out var eventType) || eventType.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            // Sent when the connection is saved in the Arr app. Answering is the whole point.
            if (eventType.GetString() == "Test")
            {
                return true;
            }

            var arrId = IdOf(root, "series") ?? IdOf(root, "movie") ?? IdOf(root, "artist");

            if (arrId == null)
            {
                _logger.Debug("Ignored a {0} event from application {1} that names no series, movie or artist", eventType.GetString(), applicationId);

                return true;
            }

            _logger.Debug("{0} from application {1} for item {2}", eventType.GetString(), applicationId, arrId);
            _commandQueue.Push(new CatalogTitleRefreshCommand { ApplicationId = applicationId, ArrId = arrId.Value });

            return true;
        }

        private static int? IdOf(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var item) &&
                   item.ValueKind == JsonValueKind.Object &&
                   item.TryGetProperty("id", out var id) &&
                   id.ValueKind == JsonValueKind.Number &&
                   id.TryGetInt32(out var value) &&
                   value > 0
                ? value
                : null;
        }
    }
}

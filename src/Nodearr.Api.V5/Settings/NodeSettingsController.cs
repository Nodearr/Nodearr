using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using Nodearr.Http.REST.Attributes;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Nodes.Transport;

namespace Nodearr.Api.V5.Settings;

public class NodeSettingsResource : RestResource
{
    public string? NodeName { get; set; }
    public RelayMode Relay { get; set; }
    public string? RelayUrl { get; set; }
    public int Port { get; set; }
    public bool PortMapping { get; set; }
    public int DirectTimeoutSeconds { get; set; }
    public string? DownloadFolder { get; set; }
    public int UploadLimitKbps { get; set; }

    // Left out by a client that does not know them, which keeps what is set.
    public int? TotalUploadLimitKbps { get; set; }
    public int? DownloadLimitKbps { get; set; }
    public int? MaxDownloads { get; set; }
    public int? MaxUploads { get; set; }
    public int? CatalogSyncInterval { get; set; }

    // Told, not asked: these are ignored when sent back.

    // The settings the environment fixes. They cannot be changed here.
    public List<string> FixedByEnvironment { get; set; } = new();

    // What an empty name and an empty download folder stand for.
    public string? DefaultNodeName { get; set; }
    public string? DefaultDownloadFolder { get; set; }
    public bool TransportRunning { get; set; }

    // Set on the answer to a save that had to restart the peer transport.
    public bool TransportRestarted { get; set; }

    public NodeSettings ToModel(NodeSettings current)
    {
        return new NodeSettings
        {
            NodeName = NodeName,
            Relay = Relay,
            RelayUrl = RelayUrl,
            Port = Port,
            PortMapping = PortMapping,
            DirectTimeoutSeconds = DirectTimeoutSeconds,
            DownloadFolder = DownloadFolder,
            UploadLimitKbps = UploadLimitKbps,
            TotalUploadLimitKbps = TotalUploadLimitKbps ?? current.TotalUploadLimitKbps,
            DownloadLimitKbps = DownloadLimitKbps ?? current.DownloadLimitKbps,
            MaxDownloads = MaxDownloads ?? current.MaxDownloads,
            MaxUploads = MaxUploads ?? current.MaxUploads,
            CatalogSyncInterval = CatalogSyncInterval ?? current.CatalogSyncInterval
        };
    }
}

[V5ApiController("settings/node")]
public class NodeSettingsController : RestController<NodeSettingsResource>
{
    private readonly INodeSettingsProvider _provider;
    private readonly INodeSettingsService _service;
    private readonly IPeerTransport _transport;

    public NodeSettingsController(INodeSettingsProvider provider, INodeSettingsService service, IPeerTransport transport)
    {
        _provider = provider;
        _service = service;
        _transport = transport;
    }

    protected override NodeSettingsResource GetResourceById(int id)
    {
        return ToResource(_provider.Get(), _transport.IsRunning, false);
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<NodeSettingsResource> GetSettings()
    {
        return TypedResults.Ok(GetResourceById(1));
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Accepted<NodeSettingsResource> SaveSettings([FromBody] NodeSettingsResource resource)
    {
        var settings = resource.ToModel(_provider.Get());
        var failures = _service.Validate(settings);

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var saved = _service.Save(settings);

        return TypedResults.Accepted((string?)null, ToResource(saved.Settings, saved.TransportRunning, saved.TransportRestarted));
    }

    private NodeSettingsResource ToResource(NodeSettings settings, bool transportRunning, bool transportRestarted)
    {
        return new NodeSettingsResource
        {
            Id = 1,
            NodeName = settings.NodeName ?? string.Empty,
            Relay = settings.Relay,
            RelayUrl = settings.RelayUrl ?? string.Empty,
            Port = settings.Port,
            PortMapping = settings.PortMapping,
            DirectTimeoutSeconds = settings.DirectTimeoutSeconds,
            DownloadFolder = settings.DownloadFolder ?? string.Empty,
            UploadLimitKbps = settings.UploadLimitKbps,
            TotalUploadLimitKbps = settings.TotalUploadLimitKbps,
            DownloadLimitKbps = settings.DownloadLimitKbps,
            MaxDownloads = settings.MaxDownloads,
            MaxUploads = settings.MaxUploads,
            CatalogSyncInterval = settings.CatalogSyncInterval,
            FixedByEnvironment = _provider.FixedByEnvironment().OrderBy(n => n).ToList(),
            DefaultNodeName = Environment.MachineName,
            DefaultDownloadFolder = _provider.DefaultDownloadFolder,
            TransportRunning = transportRunning,
            TransportRestarted = transportRestarted
        };
    }
}

using Nodearr.Http.REST;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;

namespace Nodearr.Api.V5.Settings;

public class UpdateSettingsResource : RestResource
{
    public string? Branch { get; set; }
    public bool UpdateAutomatically { get; set; }
    public UpdateMechanism UpdateMechanism { get; set; }
    public string? UpdateScriptPath { get; set; }
}

public static class UpdateSettingsResourceMapper
{
    public static UpdateSettingsResource ToResource(IConfigFileProvider config)
    {
        return new UpdateSettingsResource
        {
            Branch = config.Branch,
            UpdateAutomatically = config.UpdateAutomatically,
            UpdateMechanism = config.UpdateMechanism,
            UpdateScriptPath = config.UpdateScriptPath
        };
    }
}

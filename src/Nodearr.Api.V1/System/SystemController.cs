using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace Nodearr.Api.V1.System;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    private static readonly DateTime StartTime = DateTime.UtcNow;
    private readonly IWebHostEnvironment _environment;

    public SystemController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet("status")]
    [ProducesResponseType<SystemStatusResource>(StatusCodes.Status200OK)]
    public SystemStatusResource GetStatus()
    {
        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        return new SystemStatusResource(
            AppName: "Nodearr",
            InstanceName: "Nodearr",
            Version: assemblyVersion,
            StartTime: StartTime,
            RuntimeVersion: Environment.Version.ToString(),
            RuntimeName: RuntimeInformation.FrameworkDescription,
            OsDescription: RuntimeInformation.OSDescription,
            IsProduction: !string.Equals(
                _environment.EnvironmentName,
                "Development",
                StringComparison.OrdinalIgnoreCase));
    }
}

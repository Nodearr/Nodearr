using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Lifecycle;

namespace Nodearr.Api.V5.System;

[V5ApiController]
public class SystemController : Controller
{
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IRuntimeInfo _runtimeInfo;
    private readonly IPlatformInfo _platformInfo;
    private readonly IOsInfo _osInfo;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly IMainDatabase _database;
    private readonly ILifecycleService _lifecycleService;
    private readonly IDeploymentInfoProvider _deploymentInfoProvider;

    public SystemController(IAppFolderInfo appFolderInfo,
                            IRuntimeInfo runtimeInfo,
                            IPlatformInfo platformInfo,
                            IOsInfo osInfo,
                            IConfigFileProvider configFileProvider,
                            IMainDatabase database,
                            ILifecycleService lifecycleService,
                            IDeploymentInfoProvider deploymentInfoProvider)
    {
        _appFolderInfo = appFolderInfo;
        _runtimeInfo = runtimeInfo;
        _platformInfo = platformInfo;
        _osInfo = osInfo;
        _configFileProvider = configFileProvider;
        _database = database;
        _lifecycleService = lifecycleService;
        _deploymentInfoProvider = deploymentInfoProvider;
    }

    [HttpGet("status")]
    [Produces("application/json")]
    public Ok<SystemResource> GetStatus()
    {
        return TypedResults.Ok(new SystemResource
        {
            AppName = BuildInfo.AppName,
            InstanceName = _configFileProvider.InstanceName,
            Version = BuildInfo.Version.ToString(),
            BuildTime = BuildInfo.BuildDateTime,
            IsDebug = BuildInfo.IsDebug,
            IsProduction = RuntimeInfo.IsProduction,
            IsAdmin = _runtimeInfo.IsAdmin,
            IsUserInteractive = RuntimeInfo.IsUserInteractive,
            StartupPath = _appFolderInfo.StartUpFolder,
            AppData = _appFolderInfo.GetAppDataPath(),
            OsName = _osInfo.Name,
            OsVersion = _osInfo.Version,
            IsNetCore = true,
            IsLinux = OsInfo.IsLinux,
            IsOsx = OsInfo.IsOsx,
            IsWindows = OsInfo.IsWindows,
            IsDocker = _osInfo.IsDocker,
            Mode = _runtimeInfo.Mode,
            Branch = _configFileProvider.Branch,
            Authentication = _configFileProvider.AuthenticationMethod,
            DatabaseType = _database.DatabaseType,
            DatabaseVersion = _database.Version,
            MigrationVersion = _database.Migration,
            UrlBase = _configFileProvider.UrlBase,
            RuntimeVersion = _platformInfo.Version,
            RuntimeName = PlatformInfo.PlatformName,
            StartTime = _runtimeInfo.StartTime,
            PackageVersion = _deploymentInfoProvider.PackageVersion,
            PackageAuthor = _deploymentInfoProvider.PackageAuthor,
            PackageUpdateMechanism = _deploymentInfoProvider.PackageUpdateMechanism,
            PackageUpdateMechanismMessage = _deploymentInfoProvider.PackageUpdateMechanismMessage
        });
    }

    [HttpPost("shutdown")]
    public Ok<object> Shutdown()
    {
        Task.Factory.StartNew(() => _lifecycleService.Shutdown());
        return TypedResults.Ok<object>(new { ShuttingDown = true });
    }

    [HttpPost("restart")]
    public Ok<object> Restart()
    {
        Task.Factory.StartNew(() => _lifecycleService.Restart());
        return TypedResults.Ok<object>(new { Restarting = true });
    }
}

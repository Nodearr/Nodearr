using NLog;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Processes;
using NzbDrone.Host.AccessControl;
using IServiceProvider = NzbDrone.Common.IServiceProvider;

namespace NzbDrone.Host
{
    public interface IUtilityModeRouter
    {
        void Route(ApplicationModes applicationModes);
    }

    public class UtilityModeRouter : IUtilityModeRouter
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConsoleService _consoleService;
        private readonly IProcessProvider _processProvider;
        private readonly IRemoteAccessAdapter _remoteAccessAdapter;
        private readonly IAppFolderFactory _appFolderFactory;
        private readonly ITransportFirewallRule _transportFirewallRule;
        private readonly Logger _logger;

        public UtilityModeRouter(IServiceProvider serviceProvider,
                      IConsoleService consoleService,
                      IProcessProvider processProvider,
                      IRemoteAccessAdapter remoteAccessAdapter,
                      IAppFolderFactory appFolderFactory,
                      ITransportFirewallRule transportFirewallRule,
                      Logger logger)
        {
            _serviceProvider = serviceProvider;
            _consoleService = consoleService;
            _processProvider = processProvider;
            _remoteAccessAdapter = remoteAccessAdapter;
            _appFolderFactory = appFolderFactory;
            _transportFirewallRule = transportFirewallRule;
            _logger = logger;
        }

        public void Route(ApplicationModes applicationModes)
        {
            _logger.Info("Application mode: {0}", applicationModes);

            switch (applicationModes)
            {
                case ApplicationModes.InstallService:
                    {
                        _logger.Debug("Install Service selected");
                        if (_serviceProvider.ServiceExist(ServiceProvider.SERVICE_NAME))
                        {
                            _consoleService.PrintServiceAlreadyExist();
                        }
                        else
                        {
                            _remoteAccessAdapter.MakeAccessible(true);
                            _transportFirewallRule.Allow();
                            _serviceProvider.Install(ServiceProvider.SERVICE_NAME);
                            _serviceProvider.SetPermissions(ServiceProvider.SERVICE_NAME);

                            // Start the service and exit.
                            // Ensures that there isn't an instance of Nodearr already running that the service account cannot stop.
                            _processProvider.SpawnNewProcess("sc.exe", $"start {ServiceProvider.SERVICE_NAME}", null, true);
                        }

                        break;
                    }

                case ApplicationModes.UninstallService:
                    {
                        _logger.Debug("Uninstall Service selected");

                        // Whether Nodearr ran as a service or not, the installer let the transport in,
                        // and opened the web page's port.
                        _transportFirewallRule.Remove();
                        _transportFirewallRule.RemoveWebPage();

                        if (!_serviceProvider.ServiceExist(ServiceProvider.SERVICE_NAME))
                        {
                            _consoleService.PrintServiceDoesNotExist();
                        }
                        else
                        {
                            _serviceProvider.Uninstall(ServiceProvider.SERVICE_NAME);
                        }

                        break;
                    }

                case ApplicationModes.RegisterUrl:
                    {
                        _logger.Debug("Register URL selected");
                        _remoteAccessAdapter.MakeAccessible(false);
                        _appFolderFactory.SetPermissions();
                        _transportFirewallRule.Allow();

                        break;
                    }

                default:
                    {
                        _consoleService.PrintHelp();
                        break;
                    }
            }
        }
    }
}

using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;

namespace NzbDrone.Host.AccessControl
{
    public interface ITransportFirewallRule
    {
        void Allow();
        void Remove();

        // The rule installing opens for the web page (FirewallAdapter), taken out on uninstall.
        void RemoveWebPage();
    }

    // Other nodes reach this one on UDP, through nodearr-transport. Run by hand, Windows asks whether
    // to let it through the firewall the first time it listens. Run as a service it cannot ask, and
    // the transport would be blocked without anyone knowing. So installing Nodearr, which is done as
    // an administrator, lets the program in, and uninstalling takes the rule out again.
    public class TransportFirewallRule : ITransportFirewallRule
    {
        public const string RuleName = "Nodearr transport";

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public TransportFirewallRule(IAppFolderInfo appFolderInfo, IProcessProvider processProvider, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _processProvider = processProvider;
            _logger = logger;
        }

        public string TransportPath => Path.Combine(_appFolderInfo.StartUpFolder, "nodearr-transport.exe");

        public void Allow()
        {
            // Replaced each time, so the rule always names the program in this folder.
            Remove();

            _logger.Info("Letting UDP in to {0}", TransportPath);
            Netsh(AllowArguments(TransportPath));
        }

        public void Remove()
        {
            Netsh(RemoveArguments());
        }

        public void RemoveWebPage()
        {
            Netsh("advfirewall firewall delete rule name=\"Nodearr\"");
        }

        public static string AllowArguments(string program)
        {
            return $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{program}\" protocol=UDP profile=any enable=yes";
        }

        public static string RemoveArguments()
        {
            return $"advfirewall firewall delete rule name=\"{RuleName}\"";
        }

        private void Netsh(string arguments)
        {
            var output = _processProvider.StartAndCapture("netsh.exe", arguments);
            var lines = output.Lines.Select(l => l.Content).Where(l => l.IsNotNullOrWhiteSpace()).ConcatToString(" ");

            // Deleting a rule that is not there is not a failure.
            if (output.ExitCode != 0 && !arguments.StartsWith("advfirewall firewall delete"))
            {
                _logger.Warn("Could not let UDP in to nodearr-transport: {0}", lines);
            }
            else
            {
                _logger.Debug("netsh {0}: {1}", arguments, lines);
            }
        }
    }
}

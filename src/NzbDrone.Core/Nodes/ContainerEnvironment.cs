using System;
using System.IO;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Nodes
{
    public interface IContainerEnvironment
    {
        // Whether this Nodearr is in a container on Docker Desktop, on Windows or macOS.
        bool IsDockerDesktop { get; }
    }

    // Docker Desktop runs containers inside a Linux machine of its own, and sends each of a
    // container's outgoing UDP conversations from a different port. Two nodes find a direct route
    // by each telling the other the port it is using, so a node there cannot be found that way,
    // whatever the router is like. Nothing inside the container can change it. What Nodearr can do
    // is recognise where it is and say what will help, instead of blaming the router.
    public class ContainerEnvironment : IContainerEnvironment
    {
        private readonly Lazy<bool> _isDockerDesktop;

        public ContainerEnvironment(IOsInfo osInfo)
        {
            _isDockerDesktop = new Lazy<bool>(() => osInfo.IsDocker && LooksLikeDockerDesktop(KernelRelease()));
        }

        public bool IsDockerDesktop => _isDockerDesktop.Value;

        // The kernel's own name for itself. What .NET reports as the operating system is the
        // name of the distribution in the image, which says nothing about the host.
        private static string KernelRelease()
        {
            try
            {
                return File.ReadAllText("/proc/sys/kernel/osrelease");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        // A container shares its host's kernel, and Docker Desktop's hosts have kernels of their
        // own: Microsoft's for WSL 2, and LinuxKit everywhere else. A Linux server has neither.
        public static bool LooksLikeDockerDesktop(string kernel)
        {
            return kernel != null &&
                   (kernel.Contains("microsoft", StringComparison.OrdinalIgnoreCase) || kernel.Contains("linuxkit", StringComparison.OrdinalIgnoreCase));
        }
    }
}

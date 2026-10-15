using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Nodes;

namespace NzbDrone.Core.Test.Nodes
{
    // Telling a container on Docker Desktop from one on a Linux server, by the kernel they share
    // with their host. The names are as /proc/sys/kernel/osrelease gives them.
    [TestFixture]
    public class ContainerEnvironmentFixture
    {
        // Read in a container on Docker Desktop for Windows with the WSL 2 engine.
        [TestCase("6.18.54.1-microsoft-standard-WSL2\n")]
        [TestCase("5.15.167.4-microsoft-standard-WSL2")]

        // Docker Desktop on macOS, and on Windows with Hyper-V.
        [TestCase("6.10.14-linuxkit\n")]
        [TestCase("5.10.76-LinuxKit")]
        public void a_docker_desktop_kernel_should_be_recognised(string kernel)
        {
            ContainerEnvironment.LooksLikeDockerDesktop(kernel).Should().BeTrue();
        }

        [TestCase("6.8.0-1021-aws\n")]
        [TestCase("6.8.0-49-generic")]
        [TestCase("6.1.0-28-amd64")]
        [TestCase("4.4.302+")]
        [TestCase("6.6.51+rpt-rpi-v8")]
        [TestCase("")]
        [TestCase(null)]
        public void a_linux_servers_kernel_should_not_be(string kernel)
        {
            ContainerEnvironment.LooksLikeDockerDesktop(kernel).Should().BeFalse();
        }
    }
}

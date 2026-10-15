using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Processes;
using NzbDrone.Host;
using NzbDrone.Host.AccessControl;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class RouterTest : TestBase<UtilityModeRouter>
    {
        [SetUp]
        public void Setup()
        {
            WindowsOnly();
        }

        [Test]
        public void Route_should_call_install_service_when_application_mode_is_install()
        {
            var serviceProviderMock = Mocker.GetMock<IServiceProvider>(MockBehavior.Strict);
            serviceProviderMock.Setup(c => c.ServiceExist(ServiceProvider.SERVICE_NAME)).Returns(false);
            serviceProviderMock.Setup(c => c.Install(ServiceProvider.SERVICE_NAME));
            serviceProviderMock.Setup(c => c.SetPermissions(ServiceProvider.SERVICE_NAME));

            Mocker.GetMock<IProcessProvider>()
                  .Setup(c => c.SpawnNewProcess("sc.exe", It.IsAny<string>(), null, true));

            Mocker.GetMock<IRuntimeInfo>().SetupGet(c => c.IsUserInteractive).Returns(true);

            Subject.Route(ApplicationModes.InstallService);

            serviceProviderMock.Verify(c => c.Install(ServiceProvider.SERVICE_NAME), Times.Once());
        }

        [Test]
        public void Route_should_call_uninstall_service_when_application_mode_is_uninstall()
        {
            var serviceProviderMock = Mocker.GetMock<IServiceProvider>();
            serviceProviderMock.Setup(c => c.Uninstall(ServiceProvider.SERVICE_NAME));
            Mocker.GetMock<IRuntimeInfo>().SetupGet(c => c.IsUserInteractive).Returns(true);
            serviceProviderMock.Setup(c => c.ServiceExist(ServiceProvider.SERVICE_NAME)).Returns(true);

            Subject.Route(ApplicationModes.UninstallService);

            serviceProviderMock.Verify(c => c.Uninstall(ServiceProvider.SERVICE_NAME), Times.Once());
        }

        [Test]
        public void show_error_on_install_if_service_already_exist()
        {
            var consoleMock = Mocker.GetMock<IConsoleService>();
            var serviceMock = Mocker.GetMock<IServiceProvider>();
            Mocker.GetMock<IRuntimeInfo>().SetupGet(c => c.IsUserInteractive).Returns(true);

            consoleMock.Setup(c => c.PrintServiceAlreadyExist());
            serviceMock.Setup(c => c.ServiceExist(ServiceProvider.SERVICE_NAME)).Returns(true);

            Subject.Route(ApplicationModes.InstallService);
        }

        [Test]
        public void show_error_on_uninstall_if_service_doesnt_exist()
        {
            var consoleMock = Mocker.GetMock<IConsoleService>();
            var serviceMock = Mocker.GetMock<IServiceProvider>();
            Mocker.GetMock<IRuntimeInfo>().SetupGet(c => c.IsUserInteractive).Returns(true);

            consoleMock.Setup(c => c.PrintServiceDoesNotExist());
            serviceMock.Setup(c => c.ServiceExist(ServiceProvider.SERVICE_NAME)).Returns(false);

            Subject.Route(ApplicationModes.UninstallService);
        }

        // A service cannot answer Windows' firewall question, so installing lets the transport in.
        [Test]
        public void installing_the_service_should_let_the_transport_through_the_firewall()
        {
            Mocker.GetMock<IServiceProvider>().Setup(c => c.ServiceExist(ServiceProvider.SERVICE_NAME)).Returns(false);

            Subject.Route(ApplicationModes.InstallService);

            Mocker.GetMock<ITransportFirewallRule>().Verify(c => c.Allow(), Times.Once());
        }

        [Test]
        public void the_installers_tray_app_path_should_let_the_transport_through_the_firewall()
        {
            Subject.Route(ApplicationModes.RegisterUrl);

            Mocker.GetMock<ITransportFirewallRule>().Verify(c => c.Allow(), Times.Once());
        }

        [Test]
        public void uninstalling_should_take_the_rule_out_even_with_no_service()
        {
            Mocker.GetMock<IServiceProvider>().Setup(c => c.ServiceExist(ServiceProvider.SERVICE_NAME)).Returns(false);

            Subject.Route(ApplicationModes.UninstallService);

            Mocker.GetMock<ITransportFirewallRule>().Verify(c => c.Remove(), Times.Once());
        }

        [Test]
        public void the_rule_should_let_udp_in_to_the_transport_program_only()
        {
            TransportFirewallRule.AllowArguments(@"C:\Program Files\Nodearr\nodearr-transport.exe")
                .Should().Be(@"advfirewall firewall add rule name=""Nodearr transport"" dir=in action=allow program=""C:\Program Files\Nodearr\nodearr-transport.exe"" protocol=UDP profile=any enable=yes");
        }
    }
}

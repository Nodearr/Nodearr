using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class UpdateInstallabilityFixture : CoreTest<UpdateInstallability>
    {
        private UpdatePackage _package;

        [SetUp]
        public void Setup()
        {
            _package = new UpdatePackage { Url = "https://github.com/Nodearr/Nodearr/releases/download/v1/x.zip" };

            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.StartUpFolder).Returns(@"C:\ProgramData\Nodearr\bin".AsOsAgnostic());
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FolderWritable(It.IsAny<string>())).Returns(true);
        }

        [Test]
        public void should_replace_its_own_folder_when_it_can_write_there()
        {
            Subject.Kind(_package).Should().Be(UpdateInstallKind.BuiltIn);
        }

        [Test]
        public void should_never_update_a_container()
        {
            Mocker.GetMock<IDeploymentInfoProvider>().SetupGet(c => c.PackageUpdateMechanism).Returns(UpdateMechanism.Docker);

            Subject.Kind(_package).Should().Be(UpdateInstallKind.Docker);
        }

        [Test]
        public void should_never_update_when_set_to_docker()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.UpdateMechanism).Returns(UpdateMechanism.Docker);

            Subject.Kind(_package).Should().Be(UpdateInstallKind.Docker);
        }

        [Test]
        public void should_leave_a_package_manager_install_to_the_package_manager()
        {
            Mocker.GetMock<IDeploymentInfoProvider>().SetupGet(c => c.IsExternalUpdateMechanism).Returns(true);

            Subject.Kind(_package).Should().Be(UpdateInstallKind.External);
        }

        [Test]
        public void should_only_tell_when_it_cannot_write_its_folder()
        {
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FolderWritable(It.IsAny<string>())).Returns(false);

            Subject.Kind(_package).Should().Be(UpdateInstallKind.NotWritable);
        }

        [Test]
        public void should_say_when_the_release_has_no_package_for_this_platform()
        {
            _package.Url = null;

            Subject.Kind(_package).Should().Be(UpdateInstallKind.NoPackage);
        }

        [TestCase(@"C:\src\Nodearr\_output")]
        [TestCase(@"C:\src\Nodearr\_output\net10.0")]
        public void should_never_replace_a_development_build(string folder)
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.StartUpFolder).Returns(folder.AsOsAgnostic());

            Subject.Kind(_package).Should().Be(UpdateInstallKind.Development);
        }

        [Test]
        public void should_say_how_it_would_install_before_there_is_anything_to_install()
        {
            Subject.Kind(null).Should().Be(UpdateInstallKind.BuiltIn);
        }
    }
}

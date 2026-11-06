using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.Commands;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class UpdateQuietMomentFixture : CoreTest<UpdateQuietMoment>
    {
        private List<TransferJob> _transfers;
        private List<Upload> _uploads;
        private List<CommandModel> _commands;

        [SetUp]
        public void Setup()
        {
            _transfers = new List<TransferJob>();
            _uploads = new List<Upload>();
            _commands = new List<CommandModel>();

            Mocker.GetMock<ITransferService>().Setup(c => c.All()).Returns(() => _transfers);
            Mocker.GetMock<IUploadTracker>().Setup(c => c.All()).Returns(() => _uploads);
            Mocker.GetMock<IManageCommandQueue>().Setup(c => c.All()).Returns(() => _commands);
        }

        [Test]
        public void should_be_quiet_when_nothing_is_running()
        {
            _transfers.Add(new TransferJob { State = TransferState.Completed });
            _transfers.Add(new TransferJob { State = TransferState.Failed });
            _uploads.Add(new Upload { State = UploadState.Completed });
            _commands.Add(new CommandModel { Body = new CatalogRefreshCommand(), Status = CommandStatus.Completed });

            Subject.Busy().Should().BeNull();
        }

        [TestCase(TransferState.Queued)]
        [TestCase(TransferState.Downloading)]
        public void should_wait_for_a_transfer(TransferState state)
        {
            _transfers.Add(new TransferJob { State = state });

            Subject.Busy().Should().Contain("transfer");
        }

        [TestCase(UploadState.Waiting)]
        [TestCase(UploadState.Preparing)]
        [TestCase(UploadState.Sending)]
        public void should_wait_for_an_upload(UploadState state)
        {
            _uploads.Add(new Upload { State = state });

            Subject.Busy().Should().Contain("upload");
        }

        [TestCase(CommandStatus.Queued)]
        [TestCase(CommandStatus.Started)]
        public void should_wait_for_a_catalog_refresh(CommandStatus status)
        {
            _commands.Add(new CommandModel { Body = new CatalogRefreshCommand(), Status = status });

            Subject.Busy().Should().Contain("catalog");
        }

        [Test]
        public void should_wait_for_a_catalog_exchange_with_a_friend()
        {
            _commands.Add(new CommandModel { Body = new PeerCatalogSyncCommand(), Status = CommandStatus.Started });

            Subject.Busy().Should().Contain("catalog");
        }

        [Test]
        public void should_not_wait_for_itself()
        {
            _commands.Add(new CommandModel { Body = new ApplicationUpdateCommand(), Status = CommandStatus.Started });

            Subject.Busy().Should().BeNull();
        }
    }
}

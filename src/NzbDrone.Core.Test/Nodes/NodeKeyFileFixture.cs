using System;
using System.IO;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Test.Nodes
{
    // A key from a restored backup takes the place of the node's key when the transport next starts.
    [TestFixture]
    public class NodeKeyFileFixture
    {
        private string _folder;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "nodearr-key-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void a_restored_key_should_replace_the_old_one()
        {
            File.WriteAllText(NodeKeyFile.GetPath(_folder), "old");
            File.WriteAllText(NodeKeyFile.GetRestorePath(_folder), "restored");

            NodeKeyFile.ApplyPendingRestore(_folder, LogManager.GetCurrentClassLogger()).Should().BeTrue();

            File.ReadAllText(NodeKeyFile.GetPath(_folder)).Should().Be("restored");
            File.Exists(NodeKeyFile.GetRestorePath(_folder)).Should().BeFalse();
        }

        [Test]
        public void a_restored_key_should_be_put_in_place_where_there_was_none()
        {
            File.WriteAllText(NodeKeyFile.GetRestorePath(_folder), "restored");

            NodeKeyFile.ApplyPendingRestore(_folder, LogManager.GetCurrentClassLogger()).Should().BeTrue();

            File.ReadAllText(NodeKeyFile.GetPath(_folder)).Should().Be("restored");
        }

        [Test]
        public void the_key_should_be_left_alone_when_nothing_was_restored()
        {
            File.WriteAllText(NodeKeyFile.GetPath(_folder), "old");

            NodeKeyFile.ApplyPendingRestore(_folder, LogManager.GetCurrentClassLogger()).Should().BeFalse();

            File.ReadAllText(NodeKeyFile.GetPath(_folder)).Should().Be("old");
        }
    }
}

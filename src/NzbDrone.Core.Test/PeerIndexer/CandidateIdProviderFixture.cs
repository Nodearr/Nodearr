using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PeerIndexer
{
    [TestFixture]
    public class CandidateIdProviderFixture : CoreTest
    {
        private const string NodeId = "bc67f4010ee329c242bfeb9f355b52665cebc5f4f271ab7190946447637e0a04";

        private static CandidateIdProvider WithSecret(string secret)
        {
            var config = new Mock<IConfigService>();

            config.SetupGet(c => c.CandidateIdSecret).Returns(secret);

            return new CandidateIdProvider(config.Object);
        }

        private static readonly string SecretOne = new('1', 64);
        private static readonly string SecretTwo = new('2', 64);

        [Test]
        public void should_give_a_file_the_same_id_every_time()
        {
            WithSecret(SecretOne).For(NodeId, "e-1-501").Should().Be(WithSecret(SecretOne).For(NodeId, "e-1-501"));
        }

        [Test]
        public void should_tell_files_and_peers_apart()
        {
            var ids = WithSecret(SecretOne);

            new[] { ids.For(NodeId, "e-1-501"), ids.For(NodeId, "e-1-502"), ids.For("another-node", "e-1-501"), ids.For(NodeId + "e", "-1-501") }
                .Distinct().Should().HaveCount(4);
        }

        [Test]
        public void should_say_nothing_about_the_peer_or_the_file()
        {
            var id = WithSecret(SecretOne).For(NodeId, "e-1-501");

            id.Should().MatchRegex("^[0-9a-f]{32}$");
            id.Should().NotContain("501").And.NotContain(NodeId[..8]);
        }

        [Test]
        public void should_depend_on_this_nodes_secret()
        {
            WithSecret(SecretOne).For(NodeId, "e-1-501").Should().NotBe(WithSecret(SecretTwo).For(NodeId, "e-1-501"));
        }
    }
}

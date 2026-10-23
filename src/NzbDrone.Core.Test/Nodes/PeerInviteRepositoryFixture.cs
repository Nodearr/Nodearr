using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Nodes
{
    [TestFixture]
    public class PeerInviteRepositoryFixture : DbTest<PeerInviteRepository, PeerInvite>
    {
        private PeerInvite GivenInvite()
        {
            return Subject.Insert(new PeerInvite { SecretHash = "hash", Created = DateTime.UtcNow, Expires = DateTime.UtcNow.AddHours(1) });
        }

        [Test]
        public void an_invite_should_be_redeemed_once()
        {
            var invite = GivenInvite();

            Subject.TryRedeem(invite.Id, "bob", DateTime.UtcNow).Should().BeTrue();
            Subject.TryRedeem(invite.Id, "carol", DateTime.UtcNow).Should().BeFalse();

            var stored = Subject.Get(invite.Id);
            stored.RedeemedByNodeId.Should().Be("bob");
            stored.RedeemedAt.Should().NotBeNull();
        }

        [Test]
        public void of_many_redeeming_an_invite_at_once_one_should_succeed()
        {
            var invite = GivenInvite();

            var results = Enumerable.Range(0, 16)
                .AsParallel()
                .Select(i => Subject.TryRedeem(invite.Id, "node-" + i, DateTime.UtcNow))
                .ToList();

            results.Count(r => r).Should().Be(1);
            Subject.Get(invite.Id).RedeemedByNodeId.Should().StartWith("node-");
        }

        [Test]
        public void an_unknown_invite_should_not_be_redeemed()
        {
            Subject.TryRedeem(404, "bob", DateTime.UtcNow).Should().BeFalse();
        }
    }
}

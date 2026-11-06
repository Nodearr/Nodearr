using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class SemanticVersionFixture
    {
        [Test]
        public void should_order_versions_as_semver_does()
        {
            // semver.org's own example, with numbers that sort wrongly as text.
            var ordered = new[] { "0.9.0", "0.10.0-alpha", "0.10.0-alpha.1", "0.10.0-alpha.beta", "0.10.0-beta", "0.10.0-beta.2", "0.10.0-beta.11", "0.10.0-rc.1", "0.10.0", "0.10.1", "1.0.0" };

            var shuffled = ordered.Reverse().Select(SemanticVersion.Parse).ToList();
            shuffled.Sort();

            shuffled.Select(v => v.ToString()).Should().Equal(ordered);
        }

        [TestCase("0.10.0", "0.9.9")]
        [TestCase("0.10.0", "0.10.0-beta.1")]
        [TestCase("1.0.0", "0.99.99")]
        public void should_be_newer(string newer, string older)
        {
            (SemanticVersion.Parse(newer) > SemanticVersion.Parse(older)).Should().BeTrue();
        }

        [Test]
        public void should_ignore_build_metadata_and_a_leading_v()
        {
            SemanticVersion.Parse("v0.10.0+abc123").Should().Be(SemanticVersion.Parse("0.10.0"));
        }

        [TestCase("")]
        [TestCase("0.10")]
        [TestCase("0.10.0.0")]
        [TestCase("0.10.0-")]
        [TestCase("0.10.0-beta..1")]
        [TestCase("ten")]
        public void should_refuse_what_is_not_a_version(string text)
        {
            SemanticVersion.TryParse(text, out _).Should().BeFalse();
            Assert.Throws<FormatException>(() => SemanticVersion.Parse(text));
        }

        [Test]
        public void should_give_the_four_number_version_without_its_tag()
        {
            SemanticVersion.Parse("0.10.2-beta.1").ToVersion().Should().Be(new Version(0, 10, 2, 0));
        }
    }
}

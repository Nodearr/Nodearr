using System;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test
{
    [TestFixture]
    public class ArchiveServiceFixture : TestBase<ArchiveService>
    {
        private string _extractTo;

        [SetUp]
        public void Setup()
        {
            _extractTo = Path.Combine(TempFolder, "extract");
            Directory.CreateDirectory(_extractTo);
        }

        private string GivenZip(params (string Name, string Text)[] entries)
        {
            var path = Path.Combine(TempFolder, $"{Guid.NewGuid():N}.zip");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var (name, text) in entries)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                    writer.Write(text);
                }
            }

            return path;
        }

        [Test]
        public void should_extract_files_and_folders_inside_the_destination()
        {
            var zip = GivenZip(("config.xml", "<Config />"), ("folder/nodearr.db", "db"));

            Subject.Extract(zip, _extractTo);

            File.ReadAllText(Path.Combine(_extractTo, "config.xml")).Should().Be("<Config />");
            File.ReadAllText(Path.Combine(_extractTo, "folder", "nodearr.db")).Should().Be("db");
        }

        [TestCase("../outside.txt")]
        [TestCase("folder/../../outside.txt")]
        public void should_refuse_an_entry_that_would_land_outside_the_destination(string name)
        {
            var zip = GivenZip((name, "written where it should not be"));

            Assert.Throws<IOException>(() => Subject.Extract(zip, _extractTo));

            File.Exists(Path.Combine(TempFolder, "outside.txt")).Should().BeFalse();
        }

        [Test]
        public void should_refuse_a_sibling_folder_that_shares_the_destination_name_as_a_prefix()
        {
            // "extract-evil" starts with "extract" but is not inside it.
            var zip = GivenZip(("../extract-evil/outside.txt", "written where it should not be"));

            Assert.Throws<IOException>(() => Subject.Extract(zip, _extractTo));

            Directory.Exists(Path.Combine(TempFolder, "extract-evil")).Should().BeFalse();
        }
    }
}

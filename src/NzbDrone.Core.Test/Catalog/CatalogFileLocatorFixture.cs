using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Catalog
{
    [TestFixture]
    public class CatalogFileLocatorFixture : DbTest<CatalogFileLocator, CatalogEpisodeFile>
    {
        private static string Root => OsInfo.IsWindows ? @"C:\library\" : "/library/";

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICatalogEpisodeFileRepository>(Mocker.Resolve<CatalogEpisodeFileRepository>());
            Mocker.SetConstant<ICatalogMovieFileRepository>(Mocker.Resolve<CatalogMovieFileRepository>());

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(new List<ApplicationDefinition> { new() { Id = 1, Name = "Sonarr" }, new() { Id = 2, Name = "Radarr" } });

            // The Arr apps see the library at /tv and /movies. It is at Root here.
            Mocker.GetMock<IRemotePathMappingService>()
                  .Setup(m => m.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()))
                  .Returns((string host, OsPath path) => new OsPath(Root + path.FullPath.TrimStart('/').Replace('/', OsInfo.IsWindows ? '\\' : '/')));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns((string path) => !path.Contains("unreadable"));

            Mocker.Resolve<ICatalogEpisodeFileRepository>().InsertMany(new List<CatalogEpisodeFile>
            {
                new() { ApplicationId = 1, ArrId = 501, ArrSeriesId = 10, Path = "/tv/Pingu/e1.mkv", RelativePath = "e1.mkv" },
                new() { ApplicationId = 1, ArrId = 502, ArrSeriesId = 10, Path = "/tv/Pingu/unreadable.mkv", RelativePath = "unreadable.mkv" },
                new() { ApplicationId = 1, ArrId = 503, ArrSeriesId = 10, Path = "/tv/Pingu/gone.mkv", RelativePath = "gone.mkv", MissingSince = DateTime.UtcNow }
            });
            Mocker.Resolve<ICatalogMovieFileRepository>().Insert(new CatalogMovieFile { ApplicationId = 2, ArrId = 701, ArrMovieId = 20, Path = "/movies/Night/movie.mkv", RelativePath = "movie.mkv" });
        }

        private static readonly string[] Names = { "e-1-501", "e-1-502", "e-1-503", "m-2-701", "e-1-999", "m-1-501", "x-1-501", "e-1", "", null };

        [Test]
        public void many_at_once_should_find_each_file_the_way_one_at_a_time_does()
        {
            var many = Subject.Locate(Names);

            foreach (var name in Names)
            {
                var one = Subject.Locate(name);

                if (one == null)
                {
                    many.Should().NotContainKey(name ?? string.Empty, name);
                }
                else
                {
                    many[name].Should().BeEquivalentTo(one, name);
                }
            }
        }

        [Test]
        public void a_file_should_be_found_where_it_is_here_and_said_whether_it_can_be_read()
        {
            var many = Subject.Locate(Names);

            many.Keys.Should().BeEquivalentTo("e-1-501", "e-1-502", "m-2-701");
            many["e-1-501"].Should().Match<LocatedFile>(l => l.LocalPath == Root + Path("tv/Pingu/e1.mkv") && l.Readable);
            many["e-1-502"].Readable.Should().BeFalse();
            many["m-2-701"].LocalPath.Should().Be(Root + Path("movies/Night/movie.mkv"));
        }

        [Test]
        public void the_files_of_each_application_should_be_read_once_however_many_are_asked_for()
        {
            var names = Enumerable.Range(1, 500).Select(i => $"e-1-{i}").ToList();

            Subject.Locate(names);

            Mocker.GetMock<IApplicationFactory>().Verify(f => f.All(), Times.Once());
        }

        private static string Path(string relative) => OsInfo.IsWindows ? relative.Replace('/', '\\') : relative;
    }
}

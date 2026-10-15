using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Test.LargeLibrary
{
    // Times what grows with the size of a library, against a real database. Not run with the
    // other tests. Run it on purpose:
    //   dotnet test ... --filter "FullyQualifiedName~LargeLibraryFixture"
    // NODEARR_LARGE_SERIES, NODEARR_LARGE_EPISODES and NODEARR_LARGE_MOVIES change the size.
    [TestFixture]
    [Explicit]
    [Category("LargeLibrary")]
    public class LargeLibraryFixture : DbTest<SharingService, ShareList>
    {
        private static readonly int SeriesCount = Size("NODEARR_LARGE_SERIES", 1500);
        private static readonly int EpisodesPerSeries = Size("NODEARR_LARGE_EPISODES", 40);
        private static readonly int MovieCount = Size("NODEARR_LARGE_MOVIES", 6000);

        private readonly List<string> _report = new();
        private Peer _bob;

        private static int Size(string name, int fallback)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
        }

        private static string Root => OsInfo.IsWindows ? @"C:\library\" : "/library/";

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IShareListRepository>(Mocker.Resolve<ShareListRepository>());
            Mocker.SetConstant<IShareListItemRepository>(Mocker.Resolve<ShareListItemRepository>());
            Mocker.SetConstant<IShareListPeerRepository>(Mocker.Resolve<ShareListPeerRepository>());
            Mocker.SetConstant<IPeerRepository>(Mocker.Resolve<PeerRepository>());
            Mocker.SetConstant<ICatalogSeriesRepository>(Mocker.Resolve<CatalogSeriesRepository>());
            Mocker.SetConstant<ICatalogEpisodeRepository>(Mocker.Resolve<CatalogEpisodeRepository>());
            Mocker.SetConstant<ICatalogEpisodeFileRepository>(Mocker.Resolve<CatalogEpisodeFileRepository>());
            Mocker.SetConstant<ICatalogMovieRepository>(Mocker.Resolve<CatalogMovieRepository>());
            Mocker.SetConstant<ICatalogMovieFileRepository>(Mocker.Resolve<CatalogMovieFileRepository>());
            Mocker.SetConstant<IPeerCatalogRepository>(Mocker.Resolve<PeerCatalogRepository>());
            Mocker.SetConstant<IFileHashRepository>(Mocker.Resolve<FileHashRepository>());
            Mocker.SetConstant<ILocalHoldingsFactory>(Mocker.Resolve<LocalHoldingsFactory>());
            Mocker.SetConstant<ICatalogFileLocator>(Mocker.Resolve<CatalogFileLocator>());

            Mocker.GetMock<IApplicationFactory>().Setup(f => f.All()).Returns(new List<ApplicationDefinition> { new() { Id = 1, Name = "Sonarr" }, new() { Id = 2, Name = "Radarr" } });
            Mocker.GetMock<IRemotePathMappingService>().Setup(m => m.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>())).Returns((string host, OsPath path) => path);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<ICandidateIdProvider>().Setup(c => c.For(It.IsAny<string>(), It.IsAny<string>())).Returns((string node, string id) => node + "/" + id);

            Seed();

            _bob = Mocker.Resolve<IPeerRepository>().Insert(new Peer { NodeId = "bob", Name = "Bob", State = PeerState.Trusted, ShareAll = true, Added = DateTime.UtcNow, StateChanged = DateTime.UtcNow });
        }

        [TearDown]
        public void Report()
        {
            TestContext.Progress.WriteLine($"Library: {SeriesCount} series x {EpisodesPerSeries} episodes, {MovieCount} movies ({(SeriesCount * EpisodesPerSeries) + MovieCount} files)");
            _report.ForEach(TestContext.Progress.WriteLine);
        }

        private T Time<T>(string what, Func<T> action)
        {
            var watch = Stopwatch.StartNew();
            var result = action();
            _report.Add($"{watch.Elapsed.TotalMilliseconds,10:N0} ms  {what}");

            return result;
        }

        private void Seed()
        {
            var series = new List<CatalogSeries>();
            var episodes = new List<CatalogEpisode>();
            var episodeFiles = new List<CatalogEpisodeFile>();
            var movies = new List<CatalogMovie>();
            var movieFiles = new List<CatalogMovieFile>();
            var added = DateTime.UtcNow.AddYears(-3);

            for (var s = 1; s <= SeriesCount; s++)
            {
                series.Add(new CatalogSeries { ApplicationId = 1, ArrId = s, TvdbId = 100000 + s, ImdbId = $"tt{1000000 + s}", Title = $"Series {s}", Year = 2000 + (s % 25), Path = $"/tv/Series {s}" });

                for (var e = 1; e <= EpisodesPerSeries; e++)
                {
                    var id = ((s - 1) * EpisodesPerSeries) + e;
                    var season = ((e - 1) / 10) + 1;
                    var number = ((e - 1) % 10) + 1;

                    episodes.Add(new CatalogEpisode { ApplicationId = 1, ArrId = id, ArrSeriesId = s, SeasonNumber = season, EpisodeNumber = number, Title = $"Episode {number}", AirDate = added.AddDays(id % 900).ToString("yyyy-MM-dd"), ArrEpisodeFileId = id });
                    episodeFiles.Add(new CatalogEpisodeFile
                    {
                        ApplicationId = 1, ArrId = id, ArrSeriesId = s, SeasonNumber = season,
                        Path = $"{Root}tv{Sep}Series {s}{Sep}Season {season}{Sep}Series {s} - S{season:00}E{number:00}.mkv",
                        RelativePath = $"Season {season}/Series {s} - S{season:00}E{number:00}.mkv",
                        SceneName = $"Series.{s}.S{season:00}E{number:00}.1080p.WEB-DL.DDP5.1.H.264-GROUP",
                        Size = 1_500_000_000, Quality = "WEBDL-1080p", QualitySource = "web", Resolution = 1080, Languages = "English",
                        ReleaseGroup = "GROUP", VideoCodec = "h264", AudioCodec = "EAC3", AudioChannels = 5.1, DateAdded = added.AddMinutes(id)
                    });
                }
            }

            for (var m = 1; m <= MovieCount; m++)
            {
                movies.Add(new CatalogMovie { ApplicationId = 2, ArrId = m, TmdbId = 200000 + m, ImdbId = $"tt{2000000 + m}", Title = $"Movie {m}", Year = 1950 + (m % 75), ArrMovieFileId = m });
                movieFiles.Add(new CatalogMovieFile
                {
                    ApplicationId = 2, ArrId = m, ArrMovieId = m,
                    Path = $"{Root}movies{Sep}Movie {m}{Sep}Movie {m}.mkv", RelativePath = $"Movie {m}.mkv",
                    SceneName = $"Movie.{m}.2010.2160p.BluRay.x265-GROUP", Size = 20_000_000_000, Quality = "Bluray-2160p", QualitySource = "bluray",
                    Resolution = 2160, Languages = "English", ReleaseGroup = "GROUP", VideoCodec = "x265", AudioCodec = "TrueHD", AudioChannels = 7.1,
                    DateAdded = added.AddMinutes(m)
                });
            }

            Time("seed the catalog", () =>
            {
                Mocker.Resolve<ICatalogSeriesRepository>().InsertMany(series);
                Mocker.Resolve<ICatalogEpisodeRepository>().InsertMany(episodes);
                Mocker.Resolve<ICatalogEpisodeFileRepository>().InsertMany(episodeFiles);
                Mocker.Resolve<ICatalogMovieRepository>().InsertMany(movies);
                Mocker.Resolve<ICatalogMovieFileRepository>().InsertMany(movieFiles);

                return 0;
            });
        }

        private static string Sep => OsInfo.IsWindows ? "\\" : "/";

        private static byte[] Gzip(byte[] data)
        {
            using var output = new System.IO.MemoryStream();

            using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
            {
                gzip.Write(data);
            }

            return output.ToArray();
        }

        private static byte[] Brotli(byte[] data)
        {
            var output = new byte[System.IO.Compression.BrotliEncoder.GetMaxCompressedLength(data.Length)];

            System.IO.Compression.BrotliEncoder.TryCompress(data, output, out var written, 4, 22);

            return output[..written];
        }

        [Test]
        public void measure()
        {
            // The sending side.
            Mocker.SetConstant<ISharingService>(Subject);
            Mocker.SetConstant<IOfferRecordRepository>(Mocker.Resolve<OfferRecordRepository>());
            Mocker.SetConstant<IOfferExchange>(Mocker.Resolve<OfferExchange>());

            var offer = Time("build the offer for a node that is shared everything", () => Subject.BuildOffer(_bob));
            Time("build it again", () => Subject.BuildOffer(_bob));
            Time("check one file request against the sharing rule", () => Subject.IsOffered(_bob, "e-1-1"));

            var exchange = Mocker.Resolve<IOfferExchange>();
            var bytes = 0L;
            var serialized = new List<byte[]>();
            var pages = Time("answer every page of a catalog request, serialized", () =>
            {
                var first = exchange.Answer(_bob, new PeerMessage { Type = PeerMessageTypes.Catalog });
                serialized.Add(first.ToBytes());

                for (var page = 1; page < first.CatalogTotalPages; page++)
                {
                    serialized.Add(exchange.Answer(_bob, new PeerMessage { Type = PeerMessageTypes.Catalog, CatalogPage = page, CatalogVersion = first.CatalogVersion }).ToBytes());
                }

                return first.CatalogTotalPages;
            });
            bytes = serialized.Sum(p => (long)p.Length);
            _report.Add($"           {offer.Items.Count:N0} files offered, {pages} pages, {bytes / 1024.0 / 1024.0:N1} MB in all, {bytes / pages / 1024.0:N0} KB a page");

            // What compressing each page on its own would save. Measured only: pages are not compressed.
            foreach (var (name, compress) in new (string, Func<byte[], byte[]>)[] { ("gzip", Gzip), ("brotli, quality 4", Brotli) })
            {
                var watch = Stopwatch.StartNew();
                var compressed = serialized.Sum(p => (long)compress(p).Length);
                _report.Add($"{watch.Elapsed.TotalMilliseconds,10:N0} ms  compress every page with {name}: {compressed / 1024.0 / 1024.0:N1} MB, {(double)bytes / compressed:N1} times smaller");
            }

            // Background hashing, once every file has a hash: one pass only checks.
            var hasher = Mocker.Resolve<FileHasher>();
            Mocker.GetMock<IFileTransport>().Setup(t => t.Hash(It.IsAny<string>(), It.IsAny<HashedFile>())).Returns((string path, HashedFile known) => known ?? new HashedFile { Size = 1, ModifiedNs = 1, Blake3 = "x" });
            Mocker.GetMock<IUploadTracker>().Setup(u => u.All()).Returns(new List<Upload>());
            Time("hashing pass, first time (no real hashing, every hash stored)", () => hasher.HashShared(CancellationToken.None));
            Time("hashing pass, all hashed already", () => hasher.HashShared(CancellationToken.None));

            // Unsharing, with uploads under way.
            Mocker.GetMock<IFileTransport>().Setup(t => t.ListUploads()).Returns(Enumerable.Range(1, 3).Select(i => new FileUploadProgress { Id = $"{i}", RemoteNodeId = "bob", File = $"e-1-{i}", State = UploadState.Sending }).ToList());
            var source = Mocker.Resolve<FileSourceService>();
            Time("re-check three uploads after a sharing change", () =>
            {
                source.HandleAsync(new SharingChangedEvent());

                return 0;
            });

            // The receiving side: this node fetches its own offer as if from a peer.
            var received = 0L;
            Mocker.GetMock<IPeerService>().Setup(p => p.Ask(It.IsAny<Peer>(), It.IsAny<PeerMessage>())).Returns((Peer peer, PeerMessage message) =>
            {
                var bytes = exchange.Answer(peer, message).ToBytes();
                received += bytes.Length;

                return PeerReply.FromBytes(bytes);
            });
            var peerCatalog = Mocker.Resolve<PeerCatalogService>();
            Time("receive a peer's whole catalog and store it", () =>
            {
                peerCatalog.Sync(_bob.Id);

                return 0;
            });
            Time("receive it again, unchanged", () =>
            {
                peerCatalog.Sync(_bob.Id);

                return 0;
            });

            // One episode is upgraded in Sonarr. Only that file is sent.
            var episodeFiles = Mocker.Resolve<ICatalogEpisodeFileRepository>();
            var upgraded = episodeFiles.ForArrIds(1, new[] { 7 }).Single();
            upgraded.Quality = "Bluray-1080p";
            upgraded.QualitySource = "bluray";
            episodeFiles.Update(upgraded);

            received = 0;
            Time("receive one changed file", () =>
            {
                peerCatalog.Sync(_bob.Id);

                return 0;
            });
            _report.Add($"           {received:N0} bytes received for it");
            Time("compare every peer file with the library again after a refresh", () =>
            {
                peerCatalog.HandleAsync(new CatalogRefreshedEvent(1));

                return 0;
            });
            Time("count the peer catalog by status (Peer Catalog page)", () => peerCatalog.GetSummaries());

            // What the Arr apps ask the indexer.
            var candidates = Mocker.Resolve<CandidateService>();
            Time("indexer: one episode, by TVDB ID", () => candidates.Search(new CandidateQuery { IncludeMovies = false, TvdbId = 100000 + (SeriesCount / 2), Season = 2, Episode = "3" }));
            Time("indexer: a whole season, by TVDB ID", () => candidates.Search(new CandidateQuery { IncludeMovies = false, TvdbId = 100000 + (SeriesCount / 2), Season = 2 }));
            Time("indexer: a movie, by TMDb and IMDb ID", () => candidates.Search(new CandidateQuery { IncludeTv = false, TmdbId = 200000 + (MovieCount / 2), ImdbId = $"tt{2000000 + (MovieCount / 2)}" }));
            Time("indexer: text search", () => candidates.Search(new CandidateQuery { Text = $"Series {SeriesCount / 2}" }));
            Time("indexer: RSS (newest 100)", () => candidates.Search(new CandidateQuery()));
            Time("indexer: a grab, by candidate ID", () => candidates.Find($"bob/e-1-{EpisodesPerSeries * 3}"));
        }
    }
}

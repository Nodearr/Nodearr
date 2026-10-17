using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Lidarr;
using NzbDrone.Core.Applications.Radarr;
using NzbDrone.Core.Applications.Whisparr;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Get it in Lidarr: an artist added with only the albums sent monitored, or all of it.
    [TestFixture]
    public class LidarrLibraryAddFixture : CoreTest<Lidarr>
    {
        private const string Slint = "2869c510-2679-4a3f-bf15-e5e8b49f2f28";
        private const string Spiderland = "a6a3a5ae-8b5e-3b8b-9a3e-4e7c9d3c7b11";

        private Mock<IArrV3Proxy> _proxy;
        private List<(string Resource, JObject Body)> _posted;
        private List<(string Resource, JObject Body)> _put;
        private TimeSpan _wait;

        [SetUp]
        public void Setup()
        {
            _wait = LibraryAdd.RefreshWait;
            LibraryAdd.RefreshWait = TimeSpan.Zero;

            Subject.Definition = new ApplicationDefinition
            {
                Name = "Lidarr",
                Settings = new LidarrSettings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://lidarr:8686" }
            };

            _proxy = Mocker.GetMock<IArrV3Proxy>();
            _posted = new List<(string, JObject)>();
            _put = new List<(string, JObject)>();

            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/rootfolder")).Returns(JArray.Parse("[{\"path\":\"/music\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/qualityprofile")).Returns(JArray.Parse("[{\"id\":1,\"name\":\"Any\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/metadataprofile")).Returns(JArray.Parse("[{\"id\":1,\"name\":\"Standard\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/command")).Returns(new JArray());
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), $"/artist/lookup?term=lidarr:{Slint}")).Returns(JArray.Parse("[{\"artistName\":\"Slint\",\"foreignArtistId\":\"" + Slint + "\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/album?artistId=3"))
                  .Returns(JArray.Parse("[{\"id\":30,\"title\":\"Tweez\",\"foreignAlbumId\":\"0f0f\"},{\"id\":31,\"title\":\"Spiderland\",\"foreignAlbumId\":\"" + Spiderland.ToUpperInvariant() + "\"}]"));

            _proxy.Setup(p => p.Post(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                  .Callback<IArrConnection, string, JObject>((_, r, b) => _posted.Add((r, (JObject)b.DeepClone())))
                  .Returns<IArrConnection, string, JObject>((_, r, b) => r == "/artist" ? new JObject { ["id"] = 3 } : new JObject());
            _proxy.Setup(p => p.Get<JObject>(It.IsAny<IArrConnection>(), "/artist/3")).Returns(() => JObject.Parse("{\"id\":3,\"monitored\":false}"));
            _proxy.Setup(p => p.Put(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                  .Callback<IArrConnection, string, JObject>((_, r, b) => _put.Add((r, (JObject)b.DeepClone())))
                  .Returns(new JObject());
            _proxy.Setup(p => p.PutMany(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                  .Callback<IArrConnection, string, JObject>((_, r, b) => _put.Add((r, (JObject)b.DeepClone())));
        }

        [TearDown]
        public void Restore()
        {
            LibraryAdd.RefreshWait = _wait;
        }

        private IEnumerable<JObject> Commands(string name) => _posted.Where(p => p.Resource == "/command" && p.Body.Value<string>("name") == name).Select(p => p.Body);

        [Test]
        public void an_album_should_be_added_with_its_artist_and_only_it_monitored()
        {
            var done = Subject.AddAndSearch(new LibraryAddRequest { ForeignArtistId = Slint, Albums = new List<string> { Spiderland }, RootFolderPath = "/music", QualityProfileId = 1, MetadataProfileId = 1 });

            var added = _posted.Single(p => p.Resource == "/artist").Body;

            added.Value<int>("metadataProfileId").Should().Be(1);
            added.Value<string>("monitorNewItems").Should().Be("none");
            added["addOptions"].Value<string>("monitor").Should().Be("none");

            _put.Single(p => p.Resource == "/album/monitor").Body["albumIds"].Values<int>().Should().Equal(31);
            Commands("AlbumSearch").Single()["albumIds"].Values<int>().Should().Equal(31);
            done.Should().Be("Added to Lidarr, monitoring Spiderland, and searching");

            // Lidarr left the artist unmonitored, and it is monitored again.
            _put.Single(p => p.Resource == "/artist/3").Body.Value<bool>("monitored").Should().BeTrue();
        }

        [Test]
        public void a_whole_artist_lidarr_has_should_be_monitored_with_what_comes_later()
        {
            _proxy.Setup(p => p.Get<JObject>(It.IsAny<IArrConnection>(), "/artist/3")).Returns(JObject.Parse("{\"id\":3,\"monitored\":false}"));

            var done = Subject.AddAndSearch(new LibraryAddRequest { ForeignArtistId = Slint, Whole = true, ExistingArrId = 3 });

            _put.Single(p => p.Resource == "/artist/3").Body.Should().Match<JObject>(a => a.Value<bool>("monitored") && a.Value<string>("monitorNewItems") == "all");
            _put.Single(p => p.Resource == "/album/monitor").Body["albumIds"].Values<int>().Should().Equal(30, 31);
            Commands("ArtistSearch").Single().Value<int>("artistId").Should().Be(3);
            done.Should().StartWith("Already in Lidarr");
        }

        [Test]
        public void an_album_lidarr_does_not_list_should_be_said()
        {
            var done = Subject.AddAndSearch(new LibraryAddRequest { ForeignArtistId = Slint, Albums = new List<string> { "11111111-1111-1111-1111-111111111111" }, RootFolderPath = "/music", QualityProfileId = 1, MetadataProfileId = 1 });

            Commands("AlbumSearch").Should().BeEmpty();
            done.Should().Contain("metadata profile");
        }

        [Test]
        public void a_metadata_profile_lidarr_does_not_have_should_be_refused()
        {
            Assert.Throws<LibraryAddException>(() => Subject.AddAndSearch(new LibraryAddRequest { ForeignArtistId = Slint, RootFolderPath = "/music", QualityProfileId = 1, MetadataProfileId = 9 }));
        }
    }

    // Get it in Radarr: a movie added monitored and searched for, or searched for again.
    [TestFixture]
    public class RadarrLibraryAddFixture : CoreTest<Radarr>
    {
        private Mock<IArrV3Proxy> _proxy;
        private List<(string Resource, JObject Body)> _posted;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ApplicationDefinition
            {
                Name = "Radarr",
                Settings = new RadarrSettings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://radarr:7878" }
            };

            _proxy = Mocker.GetMock<IArrV3Proxy>();
            _posted = new List<(string, JObject)>();

            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/rootfolder")).Returns(JArray.Parse("[{\"path\":\"/movies\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/qualityprofile")).Returns(JArray.Parse("[{\"id\":1,\"name\":\"Any\"}]"));
            _proxy.Setup(p => p.Get<JObject>(It.IsAny<IArrConnection>(), "/movie/lookup/tmdb?tmdbId=10331")).Returns(JObject.Parse("{\"title\":\"Night of the Living Dead\",\"tmdbId\":10331}"));
            _proxy.Setup(p => p.Post(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                  .Callback<IArrConnection, string, JObject>((_, r, b) => _posted.Add((r, (JObject)b.DeepClone())))
                  .Returns(new JObject());
        }

        [Test]
        public void a_new_movie_should_be_added_monitored_and_searched_for()
        {
            var done = Subject.AddAndSearch(new LibraryAddRequest { TmdbId = 10331, RootFolderPath = "/movies", QualityProfileId = 1 });

            var added = _posted.Single(p => p.Resource == "/movie").Body;

            added.Value<bool>("monitored").Should().BeTrue();
            added["addOptions"].Value<bool>("searchForMovie").Should().BeTrue();
            done.Should().StartWith("Added to Radarr");
        }

        [Test]
        public void a_movie_radarr_has_should_be_monitored_and_searched_for()
        {
            _proxy.Setup(p => p.Get<JObject>(It.IsAny<IArrConnection>(), "/movie/20")).Returns(JObject.Parse("{\"id\":20,\"monitored\":false}"));

            Subject.AddAndSearch(new LibraryAddRequest { TmdbId = 10331, ExistingArrId = 20 });

            _proxy.Verify(p => p.Put(It.IsAny<IArrConnection>(), "/movie/20", It.Is<JObject>(m => m.Value<bool>("monitored"))), Times.Once());
            _posted.Single(p => p.Resource == "/command").Body.Should().Match<JObject>(c => c.Value<string>("name") == "MoviesSearch" && c["movieIds"].Values<int>().Single() == 20);
        }
    }

    // Get it in Whisparr v3: a scene found by its StashDB ID and added as Radarr adds a movie.
    [TestFixture]
    public class WhisparrV3LibraryAddFixture : CoreTest<WhisparrV3>
    {
        private const string Stash = "0d7f9e46-1c43-4d5a-9c3e-2b1d6f0a8e11";

        private Mock<IArrV3Proxy> _proxy;
        private List<(string Resource, JObject Body)> _posted;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ApplicationDefinition
            {
                Name = "Whisparr",
                Settings = new WhisparrV3Settings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://whisparr:6969" }
            };

            _proxy = Mocker.GetMock<IArrV3Proxy>();
            _posted = new List<(string, JObject)>();

            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/rootfolder")).Returns(JArray.Parse("[{\"path\":\"/adult\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/qualityprofile")).Returns(JArray.Parse("[{\"id\":1,\"name\":\"Any\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), It.Is<string>(r => r.StartsWith("/movie/lookup")))).Returns(new JArray());
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), $"/movie/lookup?term=stash%3A{Stash}")).Returns(JArray.Parse("[{\"title\":\"A scene\",\"foreignId\":\"" + Stash + "\"}]"));
            _proxy.Setup(p => p.Post(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                  .Callback<IArrConnection, string, JObject>((_, r, b) => _posted.Add((r, (JObject)b.DeepClone())))
                  .Returns(new JObject());
        }

        [Test]
        public void a_scene_should_be_found_by_its_stashdb_id_and_added_monitored()
        {
            var done = Subject.AddAndSearch(new LibraryAddRequest { StashId = Stash, TpdbId = "99", RootFolderPath = "/adult", QualityProfileId = 1 });

            var added = _posted.Single(p => p.Resource == "/movie").Body;

            added.Value<string>("title").Should().Be("A scene");
            added["addOptions"].Value<bool>("searchForMovie").Should().BeTrue();
            done.Should().Be("Added to Whisparr, monitored, and searching");

            // StashDB answered, so ThePornDB was not asked.
            _proxy.Verify(p => p.GetArray(It.IsAny<IArrConnection>(), "/movie/lookup?term=tpdb%3A99"), Times.Never());
        }

        [Test]
        public void a_film_should_be_found_by_tmdb_when_stashdb_and_theporndb_do_not_know_it()
        {
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/movie/lookup?term=tmdb%3A5")).Returns(JArray.Parse("[{\"title\":\"A film\"}]"));

            Subject.AddAndSearch(new LibraryAddRequest { StashId = "11111111-1111-1111-1111-111111111111", TmdbId = 5, RootFolderPath = "/adult", QualityProfileId = 1 });

            _posted.Single(p => p.Resource == "/movie").Body.Value<string>("title").Should().Be("A film");
        }

        [Test]
        public void what_whisparr_cannot_find_should_be_refused()
        {
            Assert.Throws<LibraryAddException>(() => Subject.AddAndSearch(new LibraryAddRequest { StashId = "11111111-1111-1111-1111-111111111111", RootFolderPath = "/adult", QualityProfileId = 1 }));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Applications.Sonarr;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Applications
{
    // Get it in Sonarr: a series added with what was sent of it monitored, then searched for.
    [TestFixture]
    public class SonarrLibraryAddFixture : CoreTest<Sonarr>
    {
        private Mock<IArrV3Proxy> _proxy;
        private List<(string Resource, JObject Body)> _posted;
        private List<(string Resource, JObject Body)> _put;
        private JArray _episodes;
        private TimeSpan _wait;

        [SetUp]
        public void Setup()
        {
            _wait = LibraryAdd.RefreshWait;
            LibraryAdd.RefreshWait = TimeSpan.Zero;

            Subject.Definition = new ApplicationDefinition
            {
                Name = "Sonarr",
                Settings = new SonarrSettings { NodearrUrl = "http://nodearr:6633", BaseUrl = "http://sonarr:8989" }
            };

            _proxy = Mocker.GetMock<IArrV3Proxy>();
            _posted = new List<(string, JObject)>();
            _put = new List<(string, JObject)>();

            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/rootfolder")).Returns(JArray.Parse("[{\"path\":\"/tv\",\"freeSpace\":100}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/qualityprofile")).Returns(JArray.Parse("[{\"id\":4,\"name\":\"HD-1080p\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/languageprofile")).Returns(JArray.Parse("[{\"id\":1,\"name\":\"English\"}]"));
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/command")).Returns(new JArray());
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/series/lookup?term=tvdb:1")).Returns(new JArray());
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/series/lookup?term=tvdb:75669"))
                  .Returns(JArray.Parse("[{\"title\":\"Pingu\",\"tvdbId\":75669,\"seasons\":[{\"seasonNumber\":0,\"monitored\":true},{\"seasonNumber\":1,\"monitored\":true},{\"seasonNumber\":2,\"monitored\":true}]}]"));

            _episodes = JArray.Parse("[" +
                "{\"id\":100,\"seasonNumber\":0,\"episodeNumber\":1,\"monitored\":false}," +
                "{\"id\":102,\"seasonNumber\":1,\"episodeNumber\":2,\"monitored\":true,\"hasFile\":true}," +
                "{\"id\":101,\"seasonNumber\":1,\"episodeNumber\":1,\"monitored\":true}," +
                "{\"id\":104,\"seasonNumber\":1,\"episodeNumber\":4,\"monitored\":true}," +
                "{\"id\":201,\"seasonNumber\":2,\"episodeNumber\":1,\"monitored\":false}]");
            _proxy.Setup(p => p.GetArray(It.IsAny<IArrConnection>(), "/episode?seriesId=7")).Returns(() => _episodes);

            _proxy.Setup(p => p.Post(It.IsAny<IArrConnection>(), It.IsAny<string>(), It.IsAny<JObject>()))
                  .Callback<IArrConnection, string, JObject>((_, r, b) => _posted.Add((r, (JObject)b.DeepClone())))
                  .Returns<IArrConnection, string, JObject>((_, r, b) => r == "/series" ? new JObject { ["id"] = 7 } : new JObject());
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

        private static LibraryAddRequest Pingu(Action<LibraryAddRequest> change = null)
        {
            var request = new LibraryAddRequest { TvdbId = 75669, RootFolderPath = "/tv", QualityProfileId = 4, SeriesType = "anime" };

            change?.Invoke(request);

            return request;
        }

        private IEnumerable<JObject> Commands(string name) => _posted.Where(p => p.Resource == "/command" && p.Body.Value<string>("name") == name).Select(p => p.Body);

        [Test]
        public void a_new_series_should_be_added_with_the_seasons_sent_monitored()
        {
            var done = Subject.AddAndSearch(Pingu(r => r.Seasons = new List<int> { 1 }));

            var added = _posted.Single(p => p.Resource == "/series").Body;

            added.Value<int>("qualityProfileId").Should().Be(4);
            added.Value<string>("rootFolderPath").Should().Be("/tv");
            added.Value<string>("seriesType").Should().Be("anime");
            added.Value<bool>("seasonFolder").Should().BeTrue();
            added.Value<int>("languageProfileId").Should().Be(1);
            added.Value<string>("monitorNewItems").Should().Be("none");
            added["seasons"].Select(s => (s.Value<int>("seasonNumber"), s.Value<bool>("monitored"))).Should().Equal((0, false), (1, true), (2, false));

            // Sonarr monitors each season's episodes as the season is, and nothing is searched by it.
            added["addOptions"]["monitor"].Should().BeNull();
            added["addOptions"].Value<bool>("searchForMissingEpisodes").Should().BeFalse();

            // Each missing episode of the season by itself: a Season Search takes only packs for an old season.
            Commands("SeasonSearch").Should().BeEmpty();
            Commands("EpisodeSearch").Single()["episodeIds"].Values<int>().Should().Equal(101, 104);
            done.Should().Be("Added to Sonarr, monitoring Season 1, and searching for 2 episodes");
        }

        [Test]
        public void a_whole_series_should_monitor_every_season_but_specials_and_what_comes_later()
        {
            Subject.AddAndSearch(Pingu(r => r.Whole = true));

            var added = _posted.Single(p => p.Resource == "/series").Body;

            added["seasons"].Select(s => s.Value<bool>("monitored")).Should().Equal(false, true, true);
            added.Value<string>("monitorNewItems").Should().Be("all");

            // The monitored episodes outside specials. Season 2's is not monitored yet in this answer.
            Commands("EpisodeSearch").Single()["episodeIds"].Values<int>().Should().Equal(101, 104);
        }

        [Test]
        public void only_what_the_friend_offers_should_be_searched_for()
        {
            Subject.AddAndSearch(Pingu(r => (r.Whole, r.OfferedEpisodes) = (true, new List<SeasonEpisode> { new() { SeasonNumber = 1, EpisodeNumber = 4 } })));

            // Monitored whole, but one search, not one for every missing episode of every season.
            Commands("EpisodeSearch").Single()["episodeIds"].Values<int>().Should().Equal(104);
        }

        [Test]
        public void single_episodes_should_be_monitored_once_sonarr_has_them_and_searched()
        {
            var done = Subject.AddAndSearch(Pingu(r => r.Episodes = new List<SeasonEpisode> { new() { SeasonNumber = 1, EpisodeNumber = 4 }, new() { SeasonNumber = 9, EpisodeNumber = 9 } }));

            var monitor = _put.Single(p => p.Resource == "/episode/monitor").Body;

            monitor["episodeIds"].Values<int>().Should().Equal(104);
            monitor.Value<bool>("monitored").Should().BeTrue();
            Commands("EpisodeSearch").Single()["episodeIds"].Values<int>().Should().Equal(104);
            done.Should().Contain("monitoring 1 episode, and searching for 1 episode").And.Contain("does not list 1 of the episodes");
        }

        [Test]
        public void a_series_sonarr_has_should_have_the_seasons_sent_monitored_on_it()
        {
            _proxy.Setup(p => p.Get<JObject>(It.IsAny<IArrConnection>(), "/series/7"))
                  .Returns(JObject.Parse("{\"id\":7,\"monitored\":false,\"seasons\":[{\"seasonNumber\":1,\"monitored\":false},{\"seasonNumber\":2,\"monitored\":false}]}"));
            _episodes = JArray.Parse("[{\"id\":201,\"seasonNumber\":2,\"episodeNumber\":1,\"monitored\":true}]");

            var done = Subject.AddAndSearch(Pingu(r => (r.ExistingArrId, r.Seasons) = (7, new List<int> { 2 })));

            _posted.Should().NotContain(p => p.Resource == "/series");

            var updated = _put.Single(p => p.Resource == "/series/7").Body;

            updated.Value<bool>("monitored").Should().BeTrue();
            updated["seasons"].Select(s => s.Value<bool>("monitored")).Should().Equal(false, true);
            done.Should().StartWith("Already in Sonarr");
        }

        [Test]
        public void a_series_tvdb_does_not_know_should_be_refused()
        {
            Assert.Throws<LibraryAddException>(() => Subject.AddAndSearch(Pingu(r => r.TvdbId = 1)));
        }

        [Test]
        public void a_root_folder_sonarr_does_not_have_should_be_refused()
        {
            Assert.Throws<LibraryAddException>(() => Subject.AddAndSearch(Pingu(r => r.RootFolderPath = "/etc")));

            _posted.Should().BeEmpty();
        }
    }
}

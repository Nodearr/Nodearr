using System;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.PeerIndexer;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PeerIndexer
{
    [TestFixture]
    public class NewznabFeedFixture : CoreTest
    {
        private static readonly XNamespace Newznab = "http://www.newznab.com/DTD/2010/feeds/attributes/";

        private static PeerCatalogItem Episode()
        {
            return new PeerCatalogItem
            {
                CandidateId = "0123456789abcdef0123456789abcdef", RemoteId = "e-1-501", PeerId = 3, MediaKind = MediaKind.Tv,
                TvdbId = 75669, ImdbId = "tt0366069", Title = "Pingu", SeasonNumber = 1, EpisodeNumbers = "4,5", Resolution = 720, Size = 123456789,
                ReleaseTitle = "Pingu.S01E04E05.720p.WEB-DL", DateAdded = new DateTime(2024, 3, 2, 10, 30, 0, DateTimeKind.Utc)
            };
        }

        private static string Attr(XElement item, string name)
        {
            return item.Elements(Newznab + "attr").Where(a => (string)a.Attribute("name") == name).Select(a => (string)a.Attribute("value")).FirstOrDefault();
        }

        [Test]
        public void an_item_should_say_what_the_arr_apps_read()
        {
            var item = NewznabFeed.Item(Episode(), "http://nodearr/get");

            item.Element("title").Value.Should().Be("Pingu.S01E04E05.720p.WEB-DL");
            item.Element("guid").Value.Should().Be("nodearr:0123456789abcdef0123456789abcdef");
            item.Element("pubDate").Value.Should().Be("Sat, 02 Mar 2024 10:30:00 GMT");
            item.Element("enclosure").Attribute("length").Value.Should().Be("123456789");
            item.Element("enclosure").Attribute("url").Value.Should().Be("http://nodearr/get");
            item.Elements(Newznab + "attr").Where(a => (string)a.Attribute("name") == "category").Select(a => (string)a.Attribute("value")).Should().Equal("5000", "5040");
            Attr(item, "size").Should().Be("123456789");
            Attr(item, "tvdbid").Should().Be("75669");
            Attr(item, "season").Should().Be("S01");
            Attr(item, "episode").Should().Be("E04");
            Attr(item, "imdb").Should().Be("0366069");
        }

        [Test]
        public void an_item_should_not_name_the_peer_or_the_peers_file()
        {
            var xml = NewznabFeed.Item(Episode(), "http://nodearr/get").ToString();

            xml.Should().NotContain("e-1-501").And.NotContain("PeerId");
        }

        [TestCase(2160, "2045")]
        [TestCase(1080, "2040")]
        [TestCase(480, "2030")]
        public void a_movie_should_be_filed_by_its_resolution(int resolution, string category)
        {
            var movie = new PeerCatalogItem { CandidateId = "aa", MediaKind = MediaKind.Movie, TmdbId = 10331, Resolution = resolution, ReleaseTitle = "x" };
            var item = NewznabFeed.Item(movie, "http://nodearr/get");

            item.Elements(Newznab + "attr").Where(a => (string)a.Attribute("name") == "category").Select(a => (string)a.Attribute("value")).Should().Equal("2000", category);
            Attr(item, "tmdbid").Should().Be("10331");
            Attr(item, "tvdbid").Should().BeNull();
        }

        [Test]
        public void the_publish_date_should_never_change_for_a_file()
        {
            var item = Episode();

            item.DateAdded = null;

            // The Arr apps blocklist by title and this date. Without a date from the peer it
            // comes from the candidate ID, so it is the same on every search.
            NewznabFeed.PublishedAt(item).Should().Be(NewznabFeed.PublishedAt(item));
            NewznabFeed.PublishedAt(item).Year.Should().Be(2020);

            item.CandidateId = "fedcba9876543210fedcba9876543210";
            NewznabFeed.PublishedAt(item).Should().NotBe(NewznabFeed.PublishedAt(Episode()));
        }

        [Test]
        public void a_ticket_should_name_its_candidate_and_be_an_nzb_the_arr_apps_accept()
        {
            var nzb = new XDocument(NewznabFeed.Ticket(Episode()));

            nzb.Root.Name.LocalName.Should().Be("nzb");
            nzb.Root.Elements().Count(e => e.Name.LocalName == "file").Should().Be(1);
            NewznabFeed.ReadTicket(nzb).Should().Be("0123456789abcdef0123456789abcdef");
            nzb.ToString().Should().NotContain("e-1-501");
        }

        [Test]
        public void something_that_is_not_a_ticket_should_name_no_candidate()
        {
            NewznabFeed.ReadTicket(XDocument.Parse("<nzb><file/></nzb>")).Should().BeNull();
        }

        [Test]
        public void tv_and_movie_searches_should_be_by_id_only()
        {
            var searching = NewznabFeed.Caps("http://nodearr").Element("searching");

            searching.Element("tv-search").Attribute("supportedParams").Value.Split(',').Should().Contain(new[] { "tvdbid", "season", "ep" }).And.NotContain("q");
            searching.Element("movie-search").Attribute("supportedParams").Value.Split(',').Should().Contain(new[] { "imdbid", "tmdbid" }).And.NotContain("q");
            searching.Element("search").Attribute("supportedParams").Value.Should().Be("q");
        }

        [Test]
        public void the_stand_in_should_cover_each_kind_asked_for_in_a_category_asked_for()
        {
            var both = NewznabFeed.ConnectionTestItems(true, true, Array.Empty<int>(), "http://nodearr").ToList();
            var tv = NewznabFeed.ConnectionTestItems(true, false, new[] { 5030, 5040 }, "http://nodearr").ToList();

            both.Should().HaveCount(2);
            both.Select(i => i.Element("guid").Value).Should().OnlyHaveUniqueItems();
            tv.Should().ContainSingle();
            tv[0].Element("title").Value.Should().Be(NewznabFeed.ConnectionTestTitle);
            tv[0].Elements(Newznab + "attr").Where(a => (string)a.Attribute("name") == "category").Select(a => (string)a.Attribute("value")).Should().Equal("5000", "5030");
        }
    }
}

using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class one_hidden_listFixture : MigrationTest<one_hidden_list>
    {
        private class ShareList250
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsHidden { get; set; }
        }

        private class ShareListItem250
        {
            public int ShareListId { get; set; }
            public int ArrId { get; set; }
        }

        [Test]
        public void what_was_ticked_on_any_hidden_list_should_end_up_on_the_one_kept()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("ShareLists").Row(new { Name = "Hidden", IsHidden = true });
                c.Insert.IntoTable("ShareLists").Row(new { Name = "Hidden", IsHidden = true });
                c.Insert.IntoTable("ShareLists").Row(new { Name = "Kids", IsHidden = false });
                c.Insert.IntoTable("ShareListItems").Row(new { ShareListId = 1, MediaKind = 0, ApplicationId = 1, ArrId = 10 });
                c.Insert.IntoTable("ShareListItems").Row(new { ShareListId = 2, MediaKind = 0, ApplicationId = 1, ArrId = 11 });
                c.Insert.IntoTable("ShareListItems").Row(new { ShareListId = 3, MediaKind = 0, ApplicationId = 1, ArrId = 12 });
            });

            var lists = db.Query<ShareList250>("SELECT \"Id\", \"Name\", \"IsHidden\" FROM \"ShareLists\"");
            var items = db.Query<ShareListItem250>("SELECT \"ShareListId\", \"ArrId\" FROM \"ShareListItems\"");

            lists.Select(l => l.Id).Should().BeEquivalentTo(new[] { 1, 3 });
            items.Where(i => i.ShareListId == 1).Select(i => i.ArrId).Should().BeEquivalentTo(new[] { 10, 11 });
            items.Single(i => i.ArrId == 12).ShareListId.Should().Be(3);
        }

        [Test]
        public void a_second_hidden_list_should_be_refused()
        {
            var db = WithMigrationTestDb(c => c.Insert.IntoTable("ShareLists").Row(new { Name = "Hidden", IsHidden = true }));

            Action second = () => db.Query("INSERT INTO \"ShareLists\" (\"Name\", \"IsHidden\") VALUES ('Hidden', 1)");

            second.Should().Throw<Exception>();
            db.Query("INSERT INTO \"ShareLists\" (\"Name\", \"IsHidden\") VALUES ('Kids', 0)");
        }
    }
}

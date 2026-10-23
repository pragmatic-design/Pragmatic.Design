using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     A navigation to a composite key (TagNote.Tag → OrderTag on OrderId and Tag): the JOIN matches every key column through the
///     <c>{Navigation}{KeyProperty}</c> foreign keys, in a predicate, a projection and an Include. Two tags of one order tell a join on the
///     first column alone from the right one. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class CompositeNavigationTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_navigation_to_a_composite_key_joins_on_every_key_column(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var orderId = databases.Seed.Orders[17].Id;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        await session.InsertAsync(new OrderTag { OrderId = orderId, Tag = "red", Note = "warm" });
        await session.InsertAsync(new OrderTag { OrderId = orderId, Tag = "blue", Note = "cold" });
        var notes = new[] { new TagNote { Id = Guid.NewGuid(), TagOrderId = orderId, TagTag = "blue", Text = "b" }, new TagNote { Id = Guid.NewGuid(), TagOrderId = orderId, TagTag = "red", Text = "r" } };
        foreach (var note in notes)
            await session.InsertAsync(note);
        var ids = notes.Select(n => n.Id).ToList();

        // In a projection and an ordering: each note reads its own tag's note, not the order's other tag.
        var projected = await session.Query<TagNote>().Where((n, ids) => ids.Contains(n.Id), ids).OrderBy(n => n.Tag!.Note).Select(n => new { n.Text, n.Tag!.Note }).ToListAsync();
        Assert.Equal([("b", "cold"), ("r", "warm")], projected.Select(p => (p.Text, p.Note)));

        // In a predicate.
        Assert.Equal(["r"], (await session.Query<TagNote>().Where((n, ids) => ids.Contains(n.Id) && n.Tag!.Note == "warm", ids).ToListAsync()).Select(n => n.Text));

        // In an Include: the tag read with the note, one row each.
        var included = await session.Query<TagNote>().Include(n => n.Tag).Where((n, ids) => ids.Contains(n.Id), ids).ToListAsync();
        Assert.Equal(2, included.Count);
        Assert.All(included, n => Assert.Equal(n.TagTag, n.Tag!.Tag));

        var sql = session.Query<TagNote>().Select(n => new { n.Text, n.Tag!.Note }).ToSql();
        Assert.Contains("AND", sql[sql.IndexOf(" ON ", StringComparison.Ordinal)..], StringComparison.Ordinal);
        transaction.Rollback();
    }
}

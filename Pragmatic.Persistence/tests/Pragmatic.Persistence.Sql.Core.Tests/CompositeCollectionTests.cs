using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>A tag's notes declared: its key columns (the loader groups on both), a count over the collection and the collection itself.</summary>
[MapFrom<OrderTag>]
public sealed class OrderTagNotesDto
{
    public Guid OrderId { get; set; }

    public string Tag { get; set; } = "";

    public int NotesCount { get; set; }

    public List<TagNoteRowDto> Notes { get; set; } = [];
}

[MapFrom<TagNote>]
public sealed class TagNoteRowDto
{
    public string Text { get; set; } = "";
}

/// <summary>
///     The collection of a parent with a composite key (OrderTag.Notes, the notes carrying TagOrderId and TagTag): a subquery correlated on both
///     columns (Any, Count), an Include (whole and taken per parent), SelectMany, a declared DTO's aggregate and collection. Two tags on one order
///     and two orders with the same tag tell a match on one column from the right one. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class CompositeCollectionTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_composite_parent_has_its_collection_matched_on_every_key_column(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var first = databases.Seed.Orders[21].Id;
        var second = databases.Seed.Orders[22].Id;
        var orders = new List<Guid> { first, second };
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        // first/red: 2 notes, first/blue: 1, second/red: none.
        foreach (var (order, tag) in new[] { (first, "red"), (first, "blue"), (second, "red") })
            await session.InsertAsync(new OrderTag { OrderId = order, Tag = tag, Note = tag });
        foreach (var (order, tag, text) in new[] { (first, "red", "r1"), (first, "red", "r2"), (first, "blue", "b1") })
            await session.InsertAsync(new TagNote { Id = Guid.NewGuid(), TagOrderId = order, TagTag = tag, Text = text });

        // A predicate and a projection: the subqueries correlate on OrderId and Tag.
        var withNotes = await session.Query<OrderTag>().Where((t, ids) => ids.Contains(t.OrderId) && t.Notes.Any(), orders).OrderBy(t => t.Tag).ToListAsync();
        Assert.Equal([(first, "blue"), (first, "red")], withNotes.Select(t => (t.OrderId, t.Tag)));
        var counts = await session.Query<OrderTag>().Where((t, ids) => ids.Contains(t.OrderId), orders).Select(t => new { t.OrderId, t.Tag, Count = t.Notes.Count }).ToListAsync();
        Assert.Equal([(first, "blue", 1), (first, "red", 2), (second, "red", 0)], counts.Select(c => (c.OrderId, c.Tag, c.Count)).OrderBy(c => c.OrderId == first ? 0 : 1).ThenBy(c => c.Tag));

        // An Include: each tag gets its own notes, not the order's other tag's or the other order's same tag's.
        var included = await session.Query<OrderTag>().Include(t => t.Notes).Where((t, ids) => ids.Contains(t.OrderId), orders).ToListAsync();
        Assert.Equal(3, included.Count);
        foreach (var tag in included)
            Assert.All(tag.Notes, n => Assert.Equal((tag.OrderId, tag.Tag), (n.TagOrderId, n.TagTag)));
        Assert.Equal([0, 1, 2], included.Select(t => t.Notes.Count).Order());

        // Taken per parent: the window partitioned by both columns.
        var taken = await session.Query<OrderTag>().Include(t => t.Notes.OrderByDescending(n => n.Text).Take(1)).Where((t, id) => t.OrderId == id, first).ToListAsync();
        Assert.Equal([("blue", "b1"), ("red", "r2")], taken.OrderBy(t => t.Tag).Select(t => (t.Tag, t.Notes.Single().Text)));

        // SelectMany: the join on both columns.
        var flat = await session.Query<OrderTag>().Where((t, ids) => ids.Contains(t.OrderId), orders).SelectMany(t => t.Notes, (t, n) => new { t.Tag, n.Text }).ToListAsync();
        Assert.Equal([("blue", "b1"), ("red", "r1"), ("red", "r2")], flat.Select(f => (f.Tag, f.Text)).Order());

        // A declared DTO: the count as a correlated subquery, the notes in a companion grouped on (OrderId, Tag).
        var dtos = await session.Query<OrderTag>().Where((t, ids) => ids.Contains(t.OrderId), orders).Project<OrderTagNotesDto>().ToListAsync();
        Assert.Equal(3, dtos.Count);
        Assert.All(dtos, d => Assert.Equal(d.NotesCount, d.Notes.Count));
        Assert.Equal(["r1", "r2"], dtos.Single(d => d.OrderId == first && d.Tag == "red").Notes.Select(n => n.Text).Order());
        Assert.Empty(dtos.Single(d => d.OrderId == second).Notes);

        // The runtime path: the same subquery, Include and declared projection from a tree; a navigation to the composite key joined on both columns.
        var runtime = session.Query<OrderTag>().AsQueryable();
        var anyRuntime = await runtime.Where(t => orders.Contains(t.OrderId) && t.Notes.Any(n => n.Text.StartsWith("r"))).ToListAsync();
        Assert.Equal([(first, "red")], anyRuntime.Select(t => (t.OrderId, t.Tag)));
        var includedRuntime = await runtime.Where(t => orders.Contains(t.OrderId)).Include(t => t.Notes.OrderByDescending(n => n.Text).Take(1)).ToListAsync();
        Assert.Equal([(first, "blue", "b1"), (first, "red", "r2"), (second, "red", "")], includedRuntime.Select(t => (t.OrderId, t.Tag, t.Notes.SingleOrDefault()?.Text ?? "")).OrderBy(t => t.OrderId == first ? 0 : 1).ThenBy(t => t.Tag));
        var projectedRuntime = await runtime.Where(t => orders.Contains(t.OrderId)).Project<OrderTag, OrderTagNotesDto>().ToListAsync();
        Assert.Equal(dtos.Select(d => (d.OrderId, d.Tag, d.NotesCount, string.Join(",", d.Notes.Select(n => n.Text).Order()))).OrderBy(d => d.ToString()),
            projectedRuntime.Select(d => (d.OrderId, d.Tag, d.NotesCount, string.Join(",", d.Notes.Select(n => n.Text).Order()))).OrderBy(d => d.ToString()));
        var notesRuntime = await session.Query<TagNote>().AsQueryable().Where(n => n.Tag!.Note == "blue" && n.TagOrderId == first).Include(n => n.Tag).ToListAsync();
        Assert.Equal([("b1", "blue")], notesRuntime.Select(n => (n.Text, n.Tag!.Tag)));

        // LoadAsync: a composite parent's collection, and the navigation to it, keyed on both columns.
        var loaded = await session.Query<OrderTag>().Where((t, ids) => ids.Contains(t.OrderId), orders).ToListAsync();
        await session.LoadAsync(loaded, t => t.Notes);
        Assert.Equal([1, 2, 0], loaded.OrderBy(t => t.OrderId == first ? 0 : 1).ThenBy(t => t.Tag).Select(t => t.Notes.Count));
        var bare = await session.Query<TagNote>().Where((n, id) => n.TagOrderId == id, first).ToListAsync();
        await session.LoadAsync(bare, n => n.Tag);
        Assert.All(bare, n => Assert.Equal((n.TagOrderId, n.TagTag), (n.Tag!.OrderId, n.Tag.Tag)));
        transaction.Rollback();
    }
}

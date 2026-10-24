using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.CompositeLinks;

/// <summary>The OrderTags table with a many-to-many side to labels: the join table holds the tag's two key columns.</summary>
[Entity]
[Table("OrderTags")]
public sealed class LabeledTag : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _collections = [];

    [Key]
    public Guid OrderId { get; set; }

    [Key]
    public string Tag { get; set; } = "";

    public string Note { get; set; } = "";

    [Version]
    public int Version { get; set; }

    [ManyToMany("OrderTagLabels", "OrderId,Tag", "LabelId")]
    public List<TagLabel> Labels { get; set; } = [];

    public void SetLabels(params TagLabel[] labels)
    {
        Labels = [.. labels];
        _collections.Add(nameof(Labels));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties { get; } = new HashSet<string>();

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified => _collections;

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties() => _collections.Clear();
}

/// <summary>The Labels table seen from the other side: its tags are a composite-key child of the same join table.</summary>
[Entity]
[Table("Labels")]
public sealed class TagLabel : ISqlEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    [ManyToMany("OrderTagLabels", "LabelId", "OrderId,Tag")]
    public List<LabeledTag> Tags { get; set; } = [];
}

/// <summary>
///     A many-to-many side with composite keys (the tag's (OrderId, Tag) in the join table): from the composite parent and towards the composite
///     child, compiled (Any, Count, Include, SelectMany) and on the runtime path (Include, a predicate), LoadAsync, the links written one by one,
///     synced by a graph save and deleted by a graph delete. Two tags of one order tell a match on one column from the right one. In a transaction,
///     rolled back.
/// </summary>
[Collection("databases")]
public class CompositeLinkTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_join_table_holds_every_key_column_of_a_composite_side(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var order = databases.Seed.Orders[41].Id;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var red = new LabeledTag { OrderId = order, Tag = "red", Note = "r", IsNew = true };
        var blue = new LabeledTag { OrderId = order, Tag = "blue", Note = "b", IsNew = true };
        var urgent = new TagLabel { Id = Guid.NewGuid(), Name = "cl-urgent" };
        var late = new TagLabel { Id = Guid.NewGuid(), Name = "cl-late" };
        await session.InsertManyAsync(new[] { urgent, late });
        await session.InsertManyAsync(new[] { red, blue });

        // One link at a time, and a graph save that syncs red's links to its list.
        await session.LinkAsync(blue, nameof(LabeledTag.Labels), late);
        red.IsNew = blue.IsNew = false;
        red.SetLabels(urgent, late);
        await session.SaveGraphAsync(red);

        // From the composite parent: the subqueries and the Include on both of the tag's columns.
        var counts = await session.Query<LabeledTag>().Where((t, id) => t.OrderId == id, order).Select(t => new { t.Tag, Count = t.Labels.Count }).ToListAsync();
        Assert.Equal([("blue", 1), ("red", 2)], counts.Select(c => (c.Tag, c.Count)).OrderBy(c => c.Tag));
        Assert.Equal(["red"], (await session.Query<LabeledTag>().Where((t, a) => t.OrderId == a.Order && t.Labels.Any(l => l.Name == a.Name), (Order: order, Name: "cl-urgent")).ToListAsync()).Select(t => t.Tag));
        var included = await session.Query<LabeledTag>().Include(t => t.Labels).Where((t, id) => t.OrderId == id, order).ToListAsync();
        Assert.Equal([("blue", "cl-late"), ("red", "cl-late,cl-urgent")], included.Select(t => (t.Tag, string.Join(",", t.Labels.Select(l => l.Name).Order(StringComparer.Ordinal)))).OrderBy(t => t.Tag));
        var flat = await session.Query<LabeledTag>().Where((t, id) => t.OrderId == id, order).SelectMany(t => t.Labels, (t, l) => new { t.Tag, l.Name }).ToListAsync();
        Assert.Equal(3, flat.Count);

        // Towards the composite child: the label's tags joined on both columns.
        var lateTags = await session.Query<TagLabel>().Include(l => l.Tags).Where((l, id) => l.Id == id, late.Id).ToListAsync();
        Assert.Equal(["blue", "red"], lateTags.Single().Tags.Select(t => t.Tag).Order(StringComparer.Ordinal));
        Assert.Equal(1, await session.Query<TagLabel>().CountAsync((l, a) => (l.Id == a.Urgent || l.Id == a.Late) && l.Tags.Any(t => t.Tag == "blue"), (Urgent: urgent.Id, Late: late.Id)));

        // The runtime path and LoadAsync, the same rows.
        var runtime = await session.Query<LabeledTag>().AsQueryable().Where(t => t.OrderId == order && t.Labels.Any(l => l.Name == "cl-late")).Include(t => t.Labels).ToListAsync();
        Assert.Equal([2, 1], runtime.OrderBy(t => t.Tag == "red" ? 0 : 1).Select(t => t.Labels.Count));
        var labels = await session.Query<TagLabel>().Where((l, id) => l.Id == id, late.Id).ToListAsync();
        await session.LoadAsync(labels, l => l.Tags);
        Assert.Equal(2, labels.Single().Tags.Count);

        // Unlink one, then delete red's graph: its links go on both columns, blue's stays.
        await session.UnlinkAsync(red, nameof(LabeledTag.Labels), urgent);
        await session.DeleteGraphAsync(red);
        var left = await session.Query<TagLabel>().Include(l => l.Tags).Where((l, id) => l.Id == id, late.Id).ToListAsync();
        Assert.Equal(["blue"], left.Single().Tags.Select(t => t.Tag));
        transaction.Rollback();
    }
}

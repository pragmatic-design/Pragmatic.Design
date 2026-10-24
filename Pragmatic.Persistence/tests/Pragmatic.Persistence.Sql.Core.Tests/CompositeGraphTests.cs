using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.CompositeGraph;

/// <summary>The OrderTags table as a tracked aggregate with a composite key (OrderId, Tag) and its notes.</summary>
[Entity]
[Table("OrderTags")]
public sealed class TrackedTag : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _collections = [];

    [Key]
    public Guid OrderId { get; set; }

    [Key]
    public string Tag { get; set; } = "";

    public string Note { get; set; } = "";

    [Version]
    public int Version { get; set; }

    public List<TrackedTagNote> Notes { get; set; } = [];

    public void AddNote(TrackedTagNote note)
    {
        (note.TagOrderId, note.TagTag) = (OrderId, Tag);
        Notes.Add(note);
        _collections.Add(nameof(Notes));
    }

    public void RemoveNote(TrackedTagNote note)
    {
        if (Notes.Remove(note))
            _collections.Add(nameof(Notes));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties { get; } = new HashSet<string>();

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified => _collections;

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties() => _collections.Clear();
}

/// <summary>A note under a tracked tag: its foreign keys those of the navigation back (TagOrderId, TagTag).</summary>
[Entity]
[Table("TagNotes")]
public sealed class TrackedTagNote : ISqlEntity, IChangeTracking
{
    public Guid Id { get; set; }
    public Guid TagOrderId { get; set; }
    public string TagTag { get; set; } = "";
    public string Text { get; set; } = "";

    public TrackedTag? Tag { get; set; }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties { get; } = new HashSet<string>();

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties()
    {
    }
}

/// <summary>
///     A graph under a composite-key parent: saved (the notes inserted with both foreign keys), changed (an orphan deleted under this tag only, matched
///     on both columns: the order's other tag keeps its notes), deleted (the cascade on both columns). In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class CompositeGraphTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_graph_under_a_composite_key_matches_both_columns(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var order = databases.Seed.Orders[31].Id;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var red = new TrackedTag { OrderId = order, Tag = "red", Note = "r", IsNew = true };
        var blue = new TrackedTag { OrderId = order, Tag = "blue", Note = "b", IsNew = true };
        var dropped = new TrackedTagNote { Id = Guid.NewGuid(), Text = "red-1", IsNew = true };
        red.AddNote(dropped);
        red.AddNote(new TrackedTagNote { Id = Guid.NewGuid(), Text = "red-2", IsNew = true });
        blue.AddNote(new TrackedTagNote { Id = Guid.NewGuid(), Text = "blue-1", IsNew = true });
        await session.SaveGraphAsync(red);
        await session.SaveGraphAsync(blue);
        Assert.Equal(["blue-1", "red-1", "red-2"], await Texts(session, order));

        // An orphan under red: deleted by (OrderId, Tag) and key, blue's note left alone; a new one inserted.
        red.RemoveNote(dropped);
        red.AddNote(new TrackedTagNote { Id = Guid.NewGuid(), Text = "red-3", IsNew = true });
        await session.SaveGraphAsync(red);
        Assert.Equal(["blue-1", "red-2", "red-3"], await Texts(session, order));

        // The cascade from red: its notes on both columns, then red; blue and its note stay.
        await session.DeleteGraphAsync(red);
        Assert.Equal(["blue-1"], await Texts(session, order));
        Assert.Equal(["blue"], (await session.Query<TrackedTag>().Where((t, id) => t.OrderId == id, order).ToListAsync()).Select(t => t.Tag));
        transaction.Rollback();
    }

    private static async Task<List<string>> Texts(SqlSession session, Guid order)
        => (await session.Query<TrackedTagNote>().Where((n, id) => n.TagOrderId == id, order).ToListAsync()).Select(n => n.Text).Order(StringComparer.Ordinal).ToList();
}

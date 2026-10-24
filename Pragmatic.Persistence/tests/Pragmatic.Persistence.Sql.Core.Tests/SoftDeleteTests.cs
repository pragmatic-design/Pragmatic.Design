using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.SoftDeletes;

/// <summary>A venue of Showcase's catalog: deleted by marking it, its halls with it; who wrote it and when stamped by the writer.</summary>
[Entity]
[Table("Venues")]
[SoftDelete(Cascade = true)]
[Auditable]
public sealed class Venue : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];
    private readonly HashSet<string> _collections = [];

    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    [RollUp<Hall>("", RollUpAggregation.Count)]
    public int HallCount { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    [Version]
    public int Version { get; set; }

    public List<Hall> Halls { get; set; } = [];

    public void Rename(string value)
    {
        Name = value;
        _modified.Add(nameof(Name));
    }

    public void AddHall(Hall hall)
    {
        hall.VenueId = Id;
        Halls.Add(hall);
        _collections.Add(nameof(Halls));
    }

    public void RemoveHall(Hall hall)
    {
        if (Halls.Remove(hall))
            _collections.Add(nameof(Halls));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties => _modified;

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified => _collections;

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties()
    {
        _modified.Clear();
        _collections.Clear();
    }
}

/// <summary>A hall of a venue: soft deleted on its own, or with its venue.</summary>
[Entity]
[Table("Halls")]
[SoftDelete]
public sealed class Hall : ISqlEntity, IChangeTracking
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public string Name { get; set; } = "";
    public int Seats { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public Venue? Venue { get; set; }

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
///     <c>[SoftDelete]</c> and <c>[Auditable]</c>: the writer stamps who and when (the user from <see cref="SqlContext" />), an update keeps the
///     Created columns, a delete marks the row (alone, as a graph's orphan, or through the cascade), every read leaves the deleted rows out
///     (compiled, runtime, includes) unless <c>IncludeDeleted()</c>, a restore brings a row back, the roll-up counts the live rows. In a transaction,
///     rolled back.
/// </summary>
[Collection("databases")]
public class SoftDeleteTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_soft_deleted_graph_stays_marked_and_stamped(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);
        var venue = new Venue { Id = Guid.NewGuid(), Name = "Riva", IsNew = true };
        var halls = new[] { "Aurora", "Borea", "Corallo" }.Select(n => new Hall { Id = Guid.NewGuid(), Name = n, Seats = 40, IsNew = true }).ToArray();
        foreach (var hall in halls)
            venue.AddHall(hall);
        using (SqlContext.Use(new SqlContext("acme", "anna")))
            await session.SaveGraphAsync(venue);

        // The insert's stamps: the user and the clock, Updated as Created.
        var stored = await Read(session, venue.Id);
        Assert.Equal(("anna", "anna", 3), (stored.CreatedBy, stored.UpdatedBy, stored.HallCount));
        Assert.True(stored.CreatedAt >= started && stored.UpdatedAt >= started);
        var created = stored.CreatedAt;

        // A save and an update stamp Updated; Created stays as stored, whatever the row says.
        using (SqlContext.Use(new SqlContext("acme", "bruno")))
        {
            venue.Rename("Riva Nord");
            await session.SaveGraphAsync(venue);
            Assert.Equal(("bruno", "anna", created), ((await Read(session, venue.Id)).UpdatedBy, (await Read(session, venue.Id)).CreatedBy, (await Read(session, venue.Id)).CreatedAt));
            venue.CreatedBy = "mallory";
            await session.UpdateAsync(venue);
            await session.UpsertAsync(venue);
        }

        stored = await Read(session, venue.Id);
        Assert.Equal(("Riva Nord", "anna", "bruno", created), (stored.Name, stored.CreatedBy, stored.UpdatedBy, stored.CreatedAt));

        // One hall deleted: marked, out of every read, out of the count; IncludeDeleted reads it.
        using (SqlContext.Use(new SqlContext("acme", "carla")))
            await session.DeleteAsync(halls[0]);
        Assert.Equal(["Borea", "Corallo"], await Live(session, venue.Id));
        var marked = (await session.Query<Hall>().IncludeDeleted().Where((h, id) => h.VenueId == id, venue.Id).ToListAsync()).Single(h => h.Id == halls[0].Id);
        Assert.Equal((true, "carla"), (marked.IsDeleted, marked.DeletedBy));
        Assert.True(marked.DeletedAt >= started);
        Assert.Equal(2, (await Read(session, venue.Id)).HallCount);

        // Restored: back in the reads and the count, the marks cleared.
        await session.RestoreAsync(halls[0]);
        Assert.Equal(["Aurora", "Borea", "Corallo"], await Live(session, venue.Id));
        Assert.Equal(3, (await Read(session, venue.Id)).HallCount);
        var restored = (await session.Query<Hall>().Where((h, id) => h.Id == id, halls[0].Id).ToListAsync()).Single();
        Assert.Equal((false, null, null), (restored.IsDeleted, restored.DeletedAt, restored.DeletedBy));

        // A graph's orphan: marked, not removed.
        venue.ResetModifiedProperties();
        venue.RemoveHall(halls[2]);
        using (SqlContext.Use(new SqlContext("acme", "dario")))
            await session.SaveGraphAsync(venue);
        Assert.Equal(["Aurora", "Borea"], await Live(session, venue.Id));
        Assert.Equal(3, (await session.Query<Hall>().IncludeDeleted().Where((h, id) => h.VenueId == id, venue.Id).ToListAsync()).Count);
        Assert.Equal(2, (await Read(session, venue.Id)).HallCount);

        // The graph deleted: the venue and its halls marked (Cascade), none removed.
        using (SqlContext.Use(new SqlContext("acme", "elena")))
            await session.DeleteGraphAsync(venue);
        Assert.Empty(await session.Query<Venue>().Where((v, id) => v.Id == id, venue.Id).ToListAsync());
        IQueryable<Venue> composed = session.Query<Venue>();
        Assert.Empty(await composed.Where(v => v.Id == venue.Id).ToListAsync());
        Assert.Empty(await Live(session, venue.Id));
        var gone = (await session.Query<Venue>().IncludeDeleted().Where((v, id) => v.Id == id, venue.Id).ToListAsync()).Single();
        Assert.Equal((true, "elena", 0), (gone.IsDeleted, gone.DeletedBy, gone.HallCount));
        var all = await session.Query<Hall>().IncludeDeleted().Where((h, id) => h.VenueId == id, venue.Id).ToListAsync();
        Assert.Equal(3, all.Count);
        Assert.All(all, h => Assert.True(h.IsDeleted));
        Assert.Equal(["dario", "elena", "elena"], all.Select(h => h.DeletedBy!).Order(StringComparer.Ordinal));
        // One instant for the graph: the halls the cascade marked carry the venue's DeletedAt, which is how a restore finds them.
        Assert.All(all.Where(h => h.DeletedBy == "elena"), h => Assert.Equal(gone.DeletedAt, h.DeletedAt));
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task An_include_and_a_subquery_read_the_live_children(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var venue = new Venue { Id = Guid.NewGuid(), Name = "Lago", IsNew = true };
        var halls = new[] { "Est", "Ovest" }.Select(n => new Hall { Id = Guid.NewGuid(), Name = n, Seats = 10, IsNew = true }).ToArray();
        foreach (var hall in halls)
            venue.AddHall(hall);
        await session.SaveGraphAsync(venue);
        await session.DeleteAsync(halls[1]);

        var included = (await session.Query<Venue>().Include(v => v.Halls).Where((v, id) => v.Id == id, venue.Id).ToListAsync()).Single();
        Assert.Equal(["Est"], included.Halls.Select(h => h.Name));
        IQueryable<Venue> runtime = session.Query<Venue>();
        var composed = (await runtime.Include(v => v.Halls).Where(v => v.Id == venue.Id).ToListAsync()).Single();
        Assert.Equal(["Est"], composed.Halls.Select(h => h.Name));
        var counted = await session.Query<Venue>().Where((v, id) => v.Id == id && session.Query<Hall>().Count(h => h.VenueId == v.Id) == 1, venue.Id).ToListAsync();
        Assert.Single(counted);
        var withDeleted = await session.Query<Venue>().Where((v, id) => v.Id == id && session.Query<Hall>().IncludeDeleted().Count(h => h.VenueId == v.Id) == 2, venue.Id).ToListAsync();
        Assert.Single(withDeleted);
        transaction.Rollback();
    }

    private static async Task<Venue> Read(SqlSession session, Guid id)
        => (await session.Query<Venue>().Where((v, key) => v.Id == key, id).ToListAsync()).Single();

    private static async Task<List<string>> Live(SqlSession session, Guid venue)
    {
        var compiled = (await session.Query<Hall>().Where((h, id) => h.VenueId == id, venue).ToListAsync()).Select(h => h.Name).Order(StringComparer.Ordinal).ToList();
        IQueryable<Hall> runtime = session.Query<Hall>();
        var composed = (await runtime.Where(h => h.VenueId == venue).ToListAsync()).Select(h => h.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(compiled, composed);
        return compiled;
    }
}

using Microsoft.EntityFrameworkCore;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     What EF Core does with <c>IgnoreQueryFilters</c> when it is handed a filter name the entity
///     does not have.
/// </summary>
/// <remarks>
///     <para>
///         The whole opt-out mechanism rests on this. <c>[WithoutFilter&lt;TRule&gt;]</c> and
///         <c>FilterMode.Background</c> put filter names into an <b>async-local</b> scope, so the names
///         reach every entity queried inside that scope — not only the one the attribute was written
///         for. A handler that lifts a rule declared on <c>Property</c> and then reads a
///         <c>Category</c> hands <c>Category</c> a name it never registered.
///     </para>
///     <para>
///         More than the attribute relies on it: <c>"Tenant"</c> is lifted for every entity in
///         <c>Background</c> mode, and most entities are not tenant entities. That everything works is
///         an argument, not a measurement — so this measures it.
///     </para>
/// </remarks>
public sealed class NamedQueryFilterLiftTests
{
    private static async Task<FilterContext> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<FilterContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var db = new FilterContext(options);
        db.Database.OpenConnection();
        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);

        db.Rows.Add(new Row { Id = 1, IsVisible = true });
        db.Rows.Add(new Row { Id = 2, IsVisible = false });
        db.Others.Add(new Other { Id = 1 });
        await db.SaveChangesAsync().ConfigureAwait(false);

        return db;
    }

    /// <summary>The filter applies when nothing lifts it — the zero the rest is measured against.</summary>
    [Fact]
    public async Task WithoutLifting_TheFilterApplies()
    {
        using var db = await CreateAsync().ConfigureAwait(true);

        var rows = await db.Rows.ToListAsync().ConfigureAwait(true);

        rows.Should().ContainSingle();
    }

    /// <summary>Naming the filter lifts that one.</summary>
    [Fact]
    public async Task LiftingByName_ReturnsTheHiddenRow()
    {
        using var db = await CreateAsync().ConfigureAwait(true);

        var rows = await db.Rows.IgnoreQueryFilters(["Visible"]).ToListAsync().ConfigureAwait(true);

        rows.Should().HaveCount(2);
    }

    /// <summary>
    ///     ⚠️ The question this file exists for: a name the entity does not have.
    /// </summary>
    /// <remarks>
    ///     If this ever throws, the generated opt-out has to narrow the names it passes to the ones the
    ///     entity actually carries, which the query executor cannot know without a generated per-entity
    ///     map. Today it does not throw, and the unknown name is ignored.
    /// </remarks>
    [Fact]
    public async Task LiftingANameTheEntityDoesNotHave_IsIgnored()
    {
        using var db = await CreateAsync().ConfigureAwait(true);

        var others = await db.Others.IgnoreQueryFilters(["Visible"]).ToListAsync().ConfigureAwait(true);

        others.Should().ContainSingle();
    }

    /// <summary>
    ///     The mixed case, which is what an async-local scope actually produces.
    /// </summary>
    /// <remarks>
    ///     One known name and one unknown one in the same call: the known one must still take effect.
    ///     Without this the test above would also pass on an EF that silently discarded the whole list
    ///     as soon as one entry did not match.
    /// </remarks>
    [Fact]
    public async Task AKnownAndAnUnknownName_StillLiftsTheKnownOne()
    {
        using var db = await CreateAsync().ConfigureAwait(true);

        var rows = await db.Rows.IgnoreQueryFilters(["Visible", "NotAFilterOfThisEntity"]).ToListAsync().ConfigureAwait(true);

        rows.Should().HaveCount(2);
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public bool IsVisible { get; set; }
    }

    private sealed class Other
    {
        public int Id { get; set; }
    }

    private sealed class FilterContext(DbContextOptions<FilterContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        public DbSet<Other> Others => Set<Other>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>().HasQueryFilter("Visible", r => r.IsVisible);
            modelBuilder.Entity<Other>();
        }
    }
}

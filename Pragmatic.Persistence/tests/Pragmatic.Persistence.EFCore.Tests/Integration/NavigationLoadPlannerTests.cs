using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Repository;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Which navigation paths are missing on a graph, and from which starting point.
/// </summary>
/// <remarks>
///     <para>
///         A merge decides what to remove by looking at what is there, and EF does not tell an empty
///         collection from one never loaded. The invoker that reads the aggregate itself includes the
///         paths it writes; one a caller <em>hands</em> an entity to does not know how much of the
///         graph came with it — and skipping the load would be silent loss.
///     </para>
///     <para>
///         ⚠️ These cases answer a single question, and the logic depends on the answer: after an
///         <c>Attach</c>, does a populated collection count as loaded? If it did, the planner would say
///         «everything is there» on a graph nobody read.
///     </para>
///     <para>
///         SQLite in-memory: the question is about the change tracker, not the SQL dialect, and a
///         real provider is more honest than the in-memory one anyway.
///     </para>
/// </remarks>
public sealed class NavigationLoadPlannerTests
{
    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static async Task<Guid> SeedCategoryWithProductsAsync(TestDbContext db)
    {
        var category = new TestCategory { Name = "tools" };
        category.Products.Add(new TestProduct { Name = "hammer" });
        category.Products.Add(new TestProduct { Name = "pincers" });

        db.Categories.Add(category);
        await db.SaveChangesAsync().ConfigureAwait(false);
        db.ChangeTracker.Clear();

        return category.PersistenceId;
    }

    /// <summary>Read with the include: nothing to load, so no query.</summary>
    [Fact]
    public async Task WhatWasIncluded_IsNotAskedForAgain()
    {
        await using var db = CreateContext();
        var id = await SeedCategoryWithProductsAsync(db).ConfigureAwait(true);

        var category = await db.Categories.Include(c => c.Products).SingleAsync(c => c.PersistenceId == id).ConfigureAwait(true);

        NavigationLoadPlanner.MissingPaths(db, category, ["Products"])
            .Should().BeEmpty("it was included, so the change tracker knows");
    }

    /// <summary>Read without include: the collection is empty and not loaded, and that must be said.</summary>
    /// <remarks>
    ///     The case that loses rows if nobody detects it: the merge would see zero children and
    ///     rewrite everything.
    /// </remarks>
    [Fact]
    public async Task WhatWasNotIncluded_IsReportedAsMissing()
    {
        await using var db = CreateContext();
        var id = await SeedCategoryWithProductsAsync(db).ConfigureAwait(true);

        var category = await db.Categories.SingleAsync(c => c.PersistenceId == id).ConfigureAwait(true);

        category.Products.Should().BeEmpty("it was not included: it looks empty");

        NavigationLoadPlanner.MissingPaths(db, category, ["Products"])
            .Should().Equal(["Products"], "and the planner tells it apart from a truly empty collection");
    }

    /// <summary>
    ///     ⚠️ The measure that decides the logic: after an <c>Attach</c>, does a populated collection
    ///     count as loaded?
    /// </summary>
    /// <remarks>
    ///     The real case is a caller that reads with <c>AsNoTracking</c> — which is what the generated
    ///     repository's <c>Query(Projection)</c> does — and hands over the result. If attaching it were
    ///     enough to make a collection that <em>was</em> populated count as «loaded», the planner would
    ///     trust a graph of unknown origin.
    /// </remarks>
    [Fact]
    public async Task AfterAttach_APopulatedCollection_IsStillReportedAsMissing()
    {
        await using var db = CreateContext();
        var id = await SeedCategoryWithProductsAsync(db).ConfigureAwait(true);

        // Detached, but with the children inside: exactly what an AsNoTracking read returns.
        var detached = await db.Categories.AsNoTracking().Include(c => c.Products).SingleAsync(c => c.PersistenceId == id).ConfigureAwait(true);
        detached.Products.Should().HaveCount(2, "the detached graph carries its children");

        db.ChangeTracker.Clear();
        db.Attach(detached);

        var missing = NavigationLoadPlanner.MissingPaths(db, detached, ["Products"]);

        missing.Should().Equal(["Products"],
            "an attached graph is not a read graph: EF does not set IsLoaded on an Attach, and "
            + "trusting the children the caller had in hand would mean accepting its snapshot as "
            + "authoritative");
    }

    /// <summary>An entity the context does not track cannot answer: it is reported missing.</summary>
    [Fact]
    public async Task ADetachedEntity_IsReportedAsMissing()
    {
        await using var db = CreateContext();
        var id = await SeedCategoryWithProductsAsync(db).ConfigureAwait(true);

        var detached = await db.Categories.AsNoTracking().SingleAsync(c => c.PersistenceId == id).ConfigureAwait(true);
        db.ChangeTracker.Clear();

        NavigationLoadPlanner.MissingPaths(db, detached, ["Products"])
            .Should().Equal(["Products"], "better one extra query than lost rows");
    }
}

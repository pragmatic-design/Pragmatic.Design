using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.DataOwnership;

/// <summary>
///     Walks through the three levels of Data Ownership one layer at a time.
///     The sample applies the filters as plain <c>Where(…)</c> calls on the
///     DbSet to keep the setup console-sized. In a real composition host the
///     SG emits OwnershipFilter / ScopedDataFilter / ComputedScopeFilter
///     classes that plug into the <c>IQueryFilter</c> pipeline, and the
///     "current user" + scope resolvers come from <c>ICurrentUser</c> +
///     <c>IUserScopeResolver</c> — here we emulate those in-process.
/// </summary>
public static class DataOwnershipSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ Data Ownership (L1 [HasOwner] + L2 [HasAccessScopes]) ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<OwnershipDbContext>()
            .UseInMemoryDatabase($"DataOwnership_{Guid.NewGuid():N}")
            .Options;

        await using var db = new OwnershipDbContext(options);

        const string alice = "user:alice";
        const string bob = "user:bob";

        // ===== L1 — [HasOwner] ===========================================
        db.Notes.AddRange(
            new OwnedNote { Id = Guid.CreateVersion7(), Title = "Alice grocery list", OwnerId = alice },
            new OwnedNote { Id = Guid.CreateVersion7(), Title = "Alice travel plans", OwnerId = alice },
            new OwnedNote { Id = Guid.CreateVersion7(), Title = "Alice reading list", OwnerId = alice },
            new OwnedNote { Id = Guid.CreateVersion7(), Title = "Bob project ideas",  OwnerId = bob });
        await db.SaveChangesAsync();

        var aliceNotes = await ApplyOwnershipFilter(db.Notes, alice, bypass: false).ToListAsync();
        var bobNotes   = await ApplyOwnershipFilter(db.Notes, bob,   bypass: false).ToListAsync();
        var adminNotes = await ApplyOwnershipFilter(db.Notes, "admin", bypass: true).ToListAsync();

        Console.WriteLine("  L1 — OwnedEntity:");
        Console.WriteLine($"    total notes in DB      : {await db.Notes.CountAsync()}");
        Console.WriteLine($"    alice sees             : {aliceNotes.Count} (expects 3)");
        Console.WriteLine($"    bob   sees             : {bobNotes.Count} (expects 1)");
        Console.WriteLine($"    admin sees (bypass)    : {adminNotes.Count} (expects 4 — view-all permission)");
        Console.WriteLine();

        // ===== L2 — [HasAccessScopes] ==========================================
        db.Projects.AddRange(
            WithScopes(new ScopedProject { Id = Guid.CreateVersion7(), Name = "Internal dashboard" },
                "team:engineering"),
            WithScopes(new ScopedProject { Id = Guid.CreateVersion7(), Name = "Marketing revamp" },
                "team:marketing"),
            WithScopes(new ScopedProject { Id = Guid.CreateVersion7(), Name = "Board briefing" },
                "role:executive"));
        await db.SaveChangesAsync();

        string[] aliceScopes = ["user:alice", "team:engineering"];
        string[] bobScopes   = ["user:bob",   "team:marketing", "role:executive"];

        var aliceProjects = ApplyScopeFilter(db.Projects, aliceScopes).ToList();
        var bobProjects   = ApplyScopeFilter(db.Projects, bobScopes).ToList();

        Console.WriteLine("  L2 — ScopedEntity:");
        Console.WriteLine($"    total projects in DB   : {await db.Projects.CountAsync()}");
        Console.WriteLine($"    alice (eng) sees       : {aliceProjects.Count} (expects 1 — engineering dashboard)");
        Console.WriteLine($"    bob (mkt+exec) sees    : {bobProjects.Count} (expects 2 — marketing + board)");
        Console.WriteLine();

        // ===== L3 — DataScopeRule ==========================================
        // The runtime also supports declarative DataScopeRule<T> instances
        // that turn a named scope into an Expression<Func<T, bool>> at query
        // time (materialized or computed). The Showcase billing boundary ships
        // EurInvoiceScopeRule + UsdInvoiceScopeRule as the canonical demo —
        // see `examples/showcase/src/Showcase.Billing/Scopes/` and the
        // `ComputedScopeFilter<T>` class it plugs into. Keeping that here
        // would require registering `IScopeMaterializer` + rule DI which
        // defeats the console-sample purpose.
        Console.WriteLine("  L3 — DataScopeRule<T>:");
        Console.WriteLine("    see examples/showcase/src/Showcase.Billing/Scopes/ for a full");
        Console.WriteLine("    specification-based scope (currency filtering on Invoice)");
        Console.WriteLine();
    }

    // In the real runtime, OwnershipFilter is SG-emitted as an IPermissionBasedFilter
    // that checks the caller's permissions before applying; here we inline the logic.
    private static IQueryable<OwnedNote> ApplyOwnershipFilter(
        IQueryable<OwnedNote> source,
        string callerId,
        bool bypass)
        => bypass ? source : source.Where(n => n.OwnerId == callerId);

    // Corresponds to ScopedDataFilter: the caller's resolved scopes must
    // intersect the row's AccessScopes. EF Core InMemory can't translate
    // list-intersect over scalar collections, so the sample pulls the rows
    // client-side first — the production EfCoreQueryExecutor handles the
    // translation on PostgreSQL / SQL Server natively.
    private static IEnumerable<ScopedProject> ApplyScopeFilter(
        IQueryable<ScopedProject> source,
        IReadOnlyList<string> callerScopes)
        => source.AsEnumerable().Where(p => p.AccessScopes.Any(callerScopes.Contains));

    private static ScopedProject WithScopes(ScopedProject project, params string[] scopes)
    {
        foreach (var s in scopes) project.AccessScopes.Add(s);
        return project;
    }
}

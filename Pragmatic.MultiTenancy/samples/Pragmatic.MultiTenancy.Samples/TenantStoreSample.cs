using Pragmatic.MultiTenancy;

namespace Pragmatic.MultiTenancy.Samples;

/// <summary>
///     Exercises the full <see cref="InMemoryTenantStore"/> lifecycle: seed, create, query
///     (by id / all / active-only), update, deactivate, and delete.
/// </summary>
public static class TenantStoreSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. InMemoryTenantStore — CRUD lifecycle (executed)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var store = new InMemoryTenantStore();

        // 8.1 Seed from startup data.
        Console.WriteLine("  8.1 Seed two tenants at startup");
        Console.WriteLine("  -------------------------------");
        store.Seed(
        [
            new TenantInfo { TenantId = "acme", TenantName = "Acme Corp", State = TenantState.Active, CreatedAt = DateTimeOffset.UtcNow },
            new TenantInfo { TenantId = "globex", TenantName = "Globex Inc", ConnectionString = "Server=db2;Database=globex", State = TenantState.Active, CreatedAt = DateTimeOffset.UtcNow }
        ]);
        Console.WriteLine($"    Seeded count : {(await store.GetAllAsync()).Count}");
        Console.WriteLine();

        // 8.2 Create a new tenant.
        Console.WriteLine("  8.2 Create a new tenant");
        Console.WriteLine("  -----------------------");
        await store.CreateAsync(new TenantInfo { TenantId = "initech", TenantName = "Initech", State = TenantState.Active, CreatedAt = DateTimeOffset.UtcNow });
        Console.WriteLine($"    After create : {(await store.GetAllAsync()).Count} tenants");
        Console.WriteLine();

        // 8.3 Lookups.
        Console.WriteLine("  8.3 Query by id");
        Console.WriteLine("  ---------------");
        var acme = await store.GetByIdAsync("acme");
        Console.WriteLine($"    GetById(\"acme\")        → {acme?.TenantName}");
        Console.WriteLine($"    GetById(\"ACME\") (CI)    → {(await store.GetByIdAsync("ACME"))?.TenantName} (case-insensitive)");
        Console.WriteLine($"    GetById(\"missing\")      → {(await store.GetByIdAsync("missing"))?.TenantName ?? "null"}");
        Console.WriteLine();

        // 8.4 Update.
        Console.WriteLine("  8.4 Update a tenant");
        Console.WriteLine("  -------------------");
        var updated = await store.UpdateAsync(acme! with { TenantName = "Acme Corporation" });
        Console.WriteLine($"    Update(\"acme\")          → {updated}; name is now \"{(await store.GetByIdAsync("acme"))!.TenantName}\"");
        Console.WriteLine($"    Update(\"missing\")       → {await store.UpdateAsync(new TenantInfo { TenantId = "missing", TenantName = "X", State = TenantState.Active, CreatedAt = DateTimeOffset.UtcNow })} (no-op)");
        Console.WriteLine();

        // 8.5 Deactivate → drops out of GetActiveAsync.
        Console.WriteLine("  8.5 Deactivate (soft-delete) and filter active");
        Console.WriteLine("  ----------------------------------------------");
        await store.DeactivateAsync("globex");
        var deactivated = await store.GetByIdAsync("globex");
        Console.WriteLine($"    globex.State           → {deactivated!.State}");
        var active = await store.GetActiveAsync();
        Console.WriteLine($"    Active tenants         → {string.Join(", ", active.Select(t => t.TenantId))}");
        Console.WriteLine();

        // 8.6 Delete.
        Console.WriteLine("  8.6 Delete permanently");
        Console.WriteLine("  ----------------------");
        Console.WriteLine($"    Delete(\"initech\")      → {await store.DeleteAsync("initech")}");
        Console.WriteLine($"    Remaining count        → {(await store.GetAllAsync()).Count}");
        Console.WriteLine();
    }
}

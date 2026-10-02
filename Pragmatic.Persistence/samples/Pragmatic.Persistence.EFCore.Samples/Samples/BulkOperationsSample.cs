using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Bulk;
using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.EFCore.UnitOfWork;

namespace Pragmatic.Persistence.EFCore.Samples.Samples;

/// <summary>
///     Demonstrates bulk operations via the generated ProductRepository.
///     BulkInsert, BulkUpsert (PK and LogicKey match), single Upsert.
/// </summary>
public static class BulkOperationsSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 5. Bulk Operations ═══");
        Console.WriteLine();

        // SQLite for real SQL execution (InMemory doesn't support raw SQL)
        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        await using var db = new SampleDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        // The repository saves through the unit of work, never through the context: that is the one
        // place that classifies what the database refused, hands over the entities' domain events, and
        // records the save. Constructed by hand here because this sample has no DI container; in an
        // application both come from the same keyed registration, so it is the same instance an
        // invoker holds — CommitScope decides commit ownership by unit-of-work identity.
        var repo = new Product.Repository(db, new EfCoreUnitOfWork(db));

        // ── BulkInsertAsync — multi-row INSERT via ADO.NET ──
        Console.WriteLine("  BulkInsertAsync — multi-row INSERT:");

        var products = Enumerable.Range(1, 5).Select(i => new Product
        {
            PersistenceId = Guid.CreateVersion7(),
            Sku = $"BULK-{i:D3}",
            Name = $"Bulk Product {i}",
            Price = 10m * i,
            StockQuantity = 100 * i,
            IsAvailable = true,
        }).ToList();

        var inserted = await repo.BulkInsertAsync(products, new BulkInsertOptions { BatchSize = 3 });
        Console.WriteLine($"    Inserted: {inserted} rows");

        db.ChangeTracker.Clear();
        foreach (var p in await repo.Set.OrderBy(p => p.Sku).ToListAsync())
            Console.WriteLine($"    [{p.Sku}] {p.Name} — {p.Price:C}");
        Console.WriteLine();

        // ── BulkUpsertAsync — PK match (update existing + insert new) ──
        Console.WriteLine("  BulkUpsertAsync — PK match:");

        var upsertBatch = new List<Product>
        {
            new() // existing PK → UPDATE
            {
                PersistenceId = products[0].PersistenceId,
                Sku = products[0].Sku,
                Name = "UPDATED Product 1",
                Price = 99.99m,
                StockQuantity = 999,
                IsAvailable = true,
            },
            new() // new PK → INSERT
            {
                PersistenceId = Guid.CreateVersion7(),
                Sku = "BULK-NEW",
                Name = "Brand New Product",
                Price = 49.99m,
                StockQuantity = 50,
                IsAvailable = true,
            }
        };

        var upserted = await repo.BulkUpsertAsync(upsertBatch);
        Console.WriteLine($"    Upserted: {upserted} rows (1 updated + 1 inserted)");

        db.ChangeTracker.Clear();
        var p1 = await repo.GetBySkuAsync("BULK-001");
        Console.WriteLine($"    [{p1!.Sku}] {p1.Name} — {p1.Price:C} (was $10.00)");
        Console.WriteLine();

        // ── BulkUpsertAsync — LogicKey match (match on SKU) ──
        Console.WriteLine("  BulkUpsertAsync — LogicKey match (SKU):");

        var upsertBySku = new List<Product>
        {
            new()
            {
                PersistenceId = Guid.CreateVersion7(), // different PK, same SKU
                Sku = "BULK-002",
                Name = "Product 2 — updated via SKU match",
                Price = 199.99m,
                StockQuantity = 1,
                IsAvailable = false,
            }
        };

        var bySkuResult = await repo.BulkUpsertAsync(
            upsertBySku, new UpsertOptions { MatchOn = UpsertMatch.LogicKey });
        Console.WriteLine($"    Upserted by SKU: {bySkuResult} rows");

        db.ChangeTracker.Clear();
        var p2 = await repo.GetBySkuAsync("BULK-002");
        Console.WriteLine($"    [{p2!.Sku}] {p2.Name} — {p2.Price:C}");
        Console.WriteLine();

        // ── UpsertAsync — single entity ──
        Console.WriteLine("  UpsertAsync — single-row upsert:");

        var single = new Product
        {
            PersistenceId = products[2].PersistenceId,
            Sku = products[2].Sku,
            Name = "Product 3 — single upsert",
            Price = 333.33m,
            StockQuantity = 33,
            IsAvailable = true,
        };

        var singleResult = await repo.UpsertAsync(single);
        Console.WriteLine($"    Result: {singleResult} row affected");

        db.ChangeTracker.Clear();
        var p3 = await repo.GetBySkuAsync("BULK-003");
        Console.WriteLine($"    [{p3!.Sku}] {p3.Name} — {p3.Price:C}");
        Console.WriteLine();

        // ── Final state ──
        Console.WriteLine("  Final state:");
        db.ChangeTracker.Clear();
        var all = await repo.Set.OrderBy(p => p.Sku).ToListAsync();
        Console.WriteLine($"    Total products: {all.Count}");
        foreach (var p in all)
            Console.WriteLine($"    [{p.Sku}] {p.Name,-40} {p.Price,10:C}");
        Console.WriteLine();

        await db.Database.CloseConnectionAsync();
    }
}

using Pragmatic.Persistence.EFCore.Samples.Entities;

namespace Pragmatic.Persistence.EFCore.Samples.Samples;

/// <summary>
///     Demonstrates soft delete and auditing entity lifecycle features.
///     [SoftDelete] marks entities as deleted without physical removal.
///     [Auditable] tracks creation and modification timestamps.
/// </summary>
public static class SoftDeleteAuditSample
{
    public static void Run()
    {
        Console.WriteLine("═══ 4. Soft Delete & Auditing ═══");
        Console.WriteLine();

        // ── Soft Delete ──
        Console.WriteLine("  [SoftDelete] — logical deletion:");

        var product = new Product
        {
            PersistenceId = Guid.CreateVersion7(),
            Sku = "DEL-001",
            Name = "Deletable Product",
            Price = 29.99m,
            IsAvailable = true
        };

        Console.WriteLine($"    Before: IsDeleted={product.IsDeleted}, DeletedAt={product.DeletedAt?.ToString() ?? "(null)"}");

        // In production, Repository.Remove() sets these automatically
        product.IsDeleted = true;
        product.DeletedAt = DateTimeOffset.UtcNow;
        product.DeletedBy = "admin@example.com";

        Console.WriteLine($"    After:  IsDeleted={product.IsDeleted}, DeletedAt={product.DeletedAt:u}");
        Console.WriteLine($"            DeletedBy={product.DeletedBy}");
        Console.WriteLine();
        Console.WriteLine("    In production:");
        Console.WriteLine("      - Repository.Remove() sets IsDeleted/DeletedAt/DeletedBy");
        Console.WriteLine("      - the SG-generated {T}.SoftDeleteFilter excludes them from queries");
        Console.WriteLine("      - QueryFilterToggle.Disable<SoftDeleteFilter>() to see deleted");
        Console.WriteLine();

        // ── Auditing ──
        Console.WriteLine("  [Auditable] — automatic timestamp tracking:");

        var customer = new Customer
        {
            PersistenceId = Guid.CreateVersion7(),
            Email = "audit@example.com",
            Name = "Audited Customer",
            // In production, AuditingInterceptor sets these on SaveChanges
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedBy = "system",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "john.doe@example.com"
        };

        Console.WriteLine($"    CreatedAt: {customer.CreatedAt:u}");
        Console.WriteLine($"    CreatedBy: {customer.CreatedBy}");
        Console.WriteLine($"    UpdatedAt: {customer.UpdatedAt:u}");
        Console.WriteLine($"    UpdatedBy: {customer.UpdatedBy}");
        Console.WriteLine();
        Console.WriteLine("    In production:");
        Console.WriteLine("      - AuditingInterceptor auto-sets on SaveChanges");
        Console.WriteLine("      - Insert: sets CreatedAt, CreatedBy, UpdatedAt, UpdatedBy");
        Console.WriteLine("      - Update: only UpdatedAt, UpdatedBy (preserves Created*)");
        Console.WriteLine("      - Uses TimeProvider for testability (FakeTimeProvider)");
        Console.WriteLine();
    }
}

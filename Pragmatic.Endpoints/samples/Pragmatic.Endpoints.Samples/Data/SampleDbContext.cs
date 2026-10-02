using Microsoft.EntityFrameworkCore;
using Pragmatic.Endpoints.Samples.Entities;

namespace Pragmatic.Endpoints.Samples.Data;

/// <summary>
///     Sample DbContext for [Endpoint] and [Autocomplete] generated endpoints.
///     The generated endpoint code injects <see cref="DbContext" /> (base class),
///     so this must be registered in DI as both SampleDbContext and DbContext.
/// </summary>
public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // [Entity] generates a read-only `Id` property that projects PersistenceId.
        // EF Core cannot use a getter-only property as primary key, so point it at PersistenceId
        // and ignore the computed projection (it re-materializes from PersistenceId on load).
        modelBuilder.Entity<Product>()
            .HasKey(p => p.PersistenceId);
        modelBuilder.Entity<Product>()
            .Ignore(p => p.Id);

        // Seed sample data for demo purposes
        modelBuilder.Entity<Product>().HasData(
            new Product { PersistenceId = Guid.Parse("a1b2c3d4-0001-0000-0000-000000000001"), Name = "Laptop Pro", Category = "Electronics", Price = 1299.99m },
            new Product { PersistenceId = Guid.Parse("a1b2c3d4-0002-0000-0000-000000000002"), Name = "Laptop Air", Category = "Electronics", Price = 999.99m },
            new Product { PersistenceId = Guid.Parse("a1b2c3d4-0003-0000-0000-000000000003"), Name = "Mechanical Keyboard", Category = "Peripherals", Price = 149.99m },
            new Product { PersistenceId = Guid.Parse("a1b2c3d4-0004-0000-0000-000000000004"), Name = "Wireless Mouse", Category = "Peripherals", Price = 49.99m },
            new Product { PersistenceId = Guid.Parse("a1b2c3d4-0005-0000-0000-000000000005"), Name = "Standing Desk", Category = "Furniture", Price = 599.99m }
        );
    }
}

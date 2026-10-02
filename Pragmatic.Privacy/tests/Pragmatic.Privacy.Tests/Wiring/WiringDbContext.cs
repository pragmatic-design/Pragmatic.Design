using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Privacy.Tests.Wiring;

/// <summary>
///     The database the generated adapters read. Deliberately hand-written and minimal: what is under
///     test is the wiring the generator produces, not the persistence generator's schema.
/// </summary>
public sealed class WiringDbContext(DbContextOptions<WiringDbContext> options) : DbContext(options)
{
    /// <summary>The subjects.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>The rows that reach a subject through a navigation.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasKey(c => c.Id);
        modelBuilder.Entity<Order>().HasKey(o => o.Id);
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId);
    }
}

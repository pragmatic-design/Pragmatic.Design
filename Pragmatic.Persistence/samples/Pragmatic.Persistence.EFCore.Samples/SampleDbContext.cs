using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Samples.Entities;

namespace Pragmatic.Persistence.EFCore.Samples;

/// <summary>
///     Sample DbContext for demonstration purposes.
///     Uses InMemory provider for simplicity.
/// </summary>
public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Address> Addresses => Set<Address>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Basic configuration for demo
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.PersistenceId);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.HasMany(e => e.Orders).WithOne(e => e.Customer).HasForeignKey(e => e.CustomerId);
            entity.HasMany(e => e.Addresses).WithOne(e => e.Customer).HasForeignKey(e => e.CustomerId);
        });

        modelBuilder.Entity<Address>(entity =>
        {
            entity.HasKey(e => e.PersistenceId);
            entity.Property(e => e.Street1).HasMaxLength(200);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(100);
            entity.Property(e => e.PostalCode).HasMaxLength(20);
            entity.Property(e => e.Country).HasMaxLength(100);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.PersistenceId);
            entity.Property(e => e.Sku).HasMaxLength(50);
            entity.HasIndex(e => e.Sku).IsUnique(); // Required for LogicKey ON CONFLICT upsert
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.PersistenceId);
            entity.Property(e => e.OrderNumber).HasMaxLength(50);
            entity.Property(e => e.Total).HasPrecision(18, 2);
            entity.HasMany(e => e.Lines).WithOne(e => e.Order).HasForeignKey(e => e.OrderId);
            entity.HasOne(e => e.ShippingAddress).WithMany().HasForeignKey(e => e.ShippingAddressId);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.HasKey(e => e.PersistenceId);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId);
        });
    }
}

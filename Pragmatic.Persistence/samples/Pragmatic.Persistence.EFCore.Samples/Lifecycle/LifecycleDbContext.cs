using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Lifecycle;

/// <summary>
///     Dedicated DbContext for the lifecycle demo. EF Core InMemory for portability.
/// </summary>
public sealed class LifecycleDbContext(DbContextOptions<LifecycleDbContext> options) : DbContext(options)
{
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionAddOn> AddOns => Set<SubscriptionAddOn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Subscription>(e =>
        {
            e.HasKey(s => s.PersistenceId);
            e.Property(s => s.SubscriptionNumber).HasMaxLength(40);
            e.Property(s => s.MonthlyPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<SubscriptionAddOn>(e =>
        {
            e.HasKey(a => a.PersistenceId);
            e.Property(a => a.Name).HasMaxLength(100);
            e.Property(a => a.Price).HasPrecision(18, 2);
        });
    }
}

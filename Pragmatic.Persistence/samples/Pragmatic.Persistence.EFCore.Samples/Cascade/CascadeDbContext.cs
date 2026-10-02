using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Cascade;

/// <summary>
///     DbContext for the cascade demo. Uses SQLite because the generated cascade handler runs
///     <c>ExecuteUpdateAsync</c>, which the InMemory provider does not support.
/// </summary>
public sealed class CascadeDbContext(DbContextOptions<CascadeDbContext> options) : DbContext(options)
{
    public DbSet<RoomRate> Rates => Set<RoomRate>();
    public DbSet<BookingCharge> Charges => Set<BookingCharge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RoomRate>(e =>
        {
            e.HasKey(r => r.PersistenceId);
            e.Property(r => r.Rate).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BookingCharge>(e =>
        {
            e.HasKey(c => c.PersistenceId);
            e.Property(c => c.UnitPrice).HasPrecision(18, 2);
        });
    }
}

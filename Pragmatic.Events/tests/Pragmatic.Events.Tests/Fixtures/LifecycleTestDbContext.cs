using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     Minimal DbContext for testing <see cref="Pragmatic.Events.EFCore.LifecycleEventsInterceptor" />.
/// </summary>
public sealed class LifecycleTestDbContext(DbContextOptions<LifecycleTestDbContext> options) : DbContext(options)
{
    public DbSet<SoftDeletableTestEntity> Entities => Set<SoftDeletableTestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SoftDeletableTestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.RaisedLifecycles);
        });
    }
}

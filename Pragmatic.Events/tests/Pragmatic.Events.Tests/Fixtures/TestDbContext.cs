using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     Minimal DbContext for testing the interceptor.
/// </summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestDbEntity> Entities => Set<TestDbEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestDbEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.DomainEvents);
        });
    }
}

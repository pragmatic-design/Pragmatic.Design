using Microsoft.EntityFrameworkCore;
using Pragmatic.Events.EFCore.Outbox;

namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     DbContext that maps the <c>__EventOutbox</c> table, for testing the outbox interceptor
///     (capture) and delivery service (claim/dispatch).
/// </summary>
public sealed class OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options) : DbContext(options)
{
    public DbSet<TestDbEntity> Entities => Set<TestDbEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestDbEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.DomainEvents);
        });

        // The outbox timestamp columns are stored as UTC ticks by EventOutboxEntryConfiguration
        // (cross-provider comparison), so the delivery query is translatable here on SQLite too.
        modelBuilder.AddEventOutbox();
    }
}

using Microsoft.EntityFrameworkCore;
using Pragmatic.Messaging.EFCore.Entities;

namespace Pragmatic.Messaging.EFCore;

/// <summary>
///     DbContext for messaging infrastructure tables: idempotency and audit.
///     Outbox tables are configured by the SG-generated DbContext via
///     <c>OutboxEntityTypeConfiguration</c> in the bridge package.
/// </summary>
/// <remarks>
///     To co-locate these tables alongside your business data, call
///     <see cref="ApplyMessagingConfigurations"/> from your own DbContext's
///     <c>OnModelCreating</c> instead of using this context directly.
/// </remarks>
public class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    /// <summary>Idempotency deduplication records.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Message audit trail entries.</summary>

    /// <summary>Durable schedule handles (restart-safe cancel on broker-native schedulers).</summary>
    public DbSet<ScheduleHandleRecord> ScheduleHandles => Set<ScheduleHandleRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyMessagingConfigurations(modelBuilder);
    }

    /// <summary>
    ///     Applies idempotency and audit entity configurations to the given model builder.
    ///     Call this from your own DbContext if you prefer to co-locate messaging tables
    ///     with your business data instead of using <see cref="MessagingDbContext"/>.
    /// </summary>
    public static void ApplyMessagingConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new IdempotencyEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new ScheduleHandleEntityTypeConfiguration());
    }
}

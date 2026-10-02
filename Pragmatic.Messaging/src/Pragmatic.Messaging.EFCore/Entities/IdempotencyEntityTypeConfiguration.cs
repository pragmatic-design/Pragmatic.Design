using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Messaging.EFCore.Entities;

/// <summary>
///     EF Core entity configuration for <see cref="IdempotencyRecord"/>.
///     Table: <c>__IdempotencyRecords</c>.
/// </summary>
public sealed class IdempotencyEntityTypeConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    /// <summary>Default table name for idempotency records.</summary>
    public const string TableName = "__IdempotencyRecords";

    // Timestamps are stored as UTC ticks (bigint), as the outbox stores its own. SQLite keeps a
    // DateTimeOffset as TEXT and cannot translate a range comparison on it, so the retention purge
    // (ProcessedAt < cutoff) would throw on every SQLite database; ticks compare on every provider.
    private static readonly ValueConverter<DateTimeOffset, long> TicksConverter =
        new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(e => e.MessageId);

        builder.Property(e => e.MessageId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.ProcessedAt)
            .HasConversion(TicksConverter)
            .IsRequired();

        builder.Property(e => e.CompletedAt).HasConversion(TicksConverter);
        builder.Property(e => e.LeaseExpiresAt).HasConversion(TicksConverter);

        // Index for efficient purge queries (older-than cleanup)
        builder.HasIndex(e => e.ProcessedAt);
    }
}

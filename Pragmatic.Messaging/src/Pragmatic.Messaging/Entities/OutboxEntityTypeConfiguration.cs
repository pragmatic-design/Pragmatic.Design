using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Messaging.Entities;

/// <summary>
///     EF Core entity configuration for <see cref="OutboxMessage"/>.
///     Applied by <c>[EnableOutbox]</c> generated DbContext configuration.
/// </summary>
public sealed class OutboxEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>
    ///     Default table name for outbox messages.
    /// </summary>
    public const string TableName = "__OutboxMessages";

    // Store all timestamps as UTC ticks (bigint). SQLite cannot translate ordering comparisons on
    // DateTimeOffset (stored as TEXT), so the delivery claim query (NextAttemptAt/ClaimedUntil range
    // predicates) fails to translate; numeric ticks compare on every provider. Mirrors the event
    // outbox, which stores ticks for the same reason. All outbox timestamps are UTC (UtcNow).
    private static readonly ValueConverter<DateTimeOffset, long> TicksConverter =
        new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.MessageType)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(e => e.Payload)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasConversion(TicksConverter)
            .IsRequired();

        builder.Property(e => e.ProcessedAt).HasConversion(TicksConverter);
        builder.Property(e => e.NextAttemptAt).HasConversion(TicksConverter);
        builder.Property(e => e.ClaimedUntil).HasConversion(TicksConverter);

        builder.Property(e => e.Error)
            .HasMaxLength(2048);

        builder.Property(e => e.CorrelationId)
            .HasMaxLength(64);

        builder.Property(e => e.TenantId)
            .HasMaxLength(128);

        builder.Property(e => e.UserId)
            .HasMaxLength(128);

        builder.Property(e => e.ClaimedBy)
            .HasMaxLength(128);

        // Index for efficient polling: pending + backoff-eligible messages, ordered by creation.
        // ClaimedUntil participates so the atomic claim query (unclaimed OR expired claim) is covered.
        builder.HasIndex(e => new { e.ProcessedAt, e.NextAttemptAt, e.ClaimedUntil, e.CreatedAt });

        // Index for correlation-based lookups
        builder.HasIndex(e => e.CorrelationId);
    }
}

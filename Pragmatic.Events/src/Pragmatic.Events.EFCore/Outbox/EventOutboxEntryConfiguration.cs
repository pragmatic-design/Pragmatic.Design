using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Events.EFCore.Outbox;

/// <summary>
///     EF Core mapping for <see cref="EventOutboxEntry"/>. Apply it from the consumer
///     DbContext's <c>OnModelCreating</c> via <see cref="EventOutboxExtensions.AddEventOutbox"/>.
/// </summary>
public sealed class EventOutboxEntryConfiguration : IEntityTypeConfiguration<EventOutboxEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EventOutboxEntry> builder)
    {
        builder.ToTable("__EventOutbox");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(512);
        // No HasMaxLength on the payload → EF maps it to each provider's unbounded large-string type
        // (PostgreSQL text, SQL Server nvarchar(max), SQLite TEXT), so large event payloads are stored
        // without truncation and without a provider-specific column type ("text" is deprecated on SQL Server).
        builder.Property(e => e.Payload).IsRequired();

        // Originating tenant, restored onto the delivery scope so handlers run tenant-scoped.
        builder.Property(e => e.TenantId).HasMaxLength(128);

        builder.Property(e => e.ClaimedBy).HasMaxLength(128);

        // W3C traceparent is 55 chars; 64 leaves headroom without an unbounded column.
        builder.Property(e => e.TraceParent).HasMaxLength(64);

        // Store timestamps as UTC ticks. SQLite cannot translate DateTimeOffset comparisons/ordering,
        // so the delivery eligibility query (ClaimedUntil < now, OrderBy CreatedAt) would throw there;
        // ticks are a monotonic integer that every provider compares and orders natively. The outbox
        // is internal infrastructure, so the native timestamptz representation is not required.
        builder.Property(e => e.OccurredAt).HasConversion(TicksConverter);
        builder.Property(e => e.CreatedAt).HasConversion(TicksConverter);
        builder.Property(e => e.ProcessedAt).HasConversion(NullableTicksConverter);
        builder.Property(e => e.ClaimedUntil).HasConversion(NullableTicksConverter);

        // Supports the delivery claim query: pending, re-claimable rows oldest first.
        builder.HasIndex(e => new { e.ProcessedAt, e.ClaimedUntil, e.CreatedAt });
    }

    private static readonly ValueConverter<DateTimeOffset, long> TicksConverter =
        new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableTicksConverter =
        new(v => v == null ? null : v.Value.UtcTicks,
            v => v == null ? null : new DateTimeOffset(v.Value, TimeSpan.Zero));
}

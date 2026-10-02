using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Messaging.Sql.Entities;

/// <summary>EF configuration for the queue table <c>__TransportMessages</c>.</summary>
public sealed class TransportMessageEntityTypeConfiguration : IEntityTypeConfiguration<TransportMessage>
{
    public const string TableName = "__TransportMessages";

    public void Configure(EntityTypeBuilder<TransportMessage> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MessageId).HasMaxLength(64).IsRequired();
        builder.Property(m => m.QueueName).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Topic).HasMaxLength(256);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(64);
        builder.Property(m => m.TenantId).HasMaxLength(128);
        builder.Property(m => m.UserId).HasMaxLength(128);
        builder.Property(m => m.SourceBoundary).HasMaxLength(128);
        builder.Property(m => m.BusName).HasMaxLength(128);
        builder.Property(m => m.LockedBy).HasMaxLength(128);
        builder.Property(m => m.LastError).HasMaxLength(2048);

        // Covers the claim scan: eligible rows per queue in enqueue order.
        builder.HasIndex(m => new { m.QueueName, m.VisibleAt, m.Id })
            .HasDatabaseName("IX___TransportMessages_Claim");

        // Restart-safe schedule cancellation (DELETE by token).
        builder.HasIndex(m => m.SchedulingTokenId)
            .HasDatabaseName("IX___TransportMessages_SchedulingToken");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Messaging.Sql.Entities;

/// <summary>EF configuration for the dead-letter table <c>__TransportDeadLetters</c>.</summary>
public sealed class TransportDeadLetterEntityTypeConfiguration : IEntityTypeConfiguration<TransportDeadLetter>
{
    public const string TableName = "__TransportDeadLetters";

    public void Configure(EntityTypeBuilder<TransportDeadLetter> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(d => d.Id);
        // Moved rows keep their queue-table identity — no re-generation on insert.
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.MessageId).HasMaxLength(64).IsRequired();
        builder.Property(d => d.QueueName).HasMaxLength(256).IsRequired();
        builder.Property(d => d.Topic).HasMaxLength(256);
        builder.Property(d => d.Payload).IsRequired();
        builder.Property(d => d.CorrelationId).HasMaxLength(64);
        builder.Property(d => d.TenantId).HasMaxLength(128);
        builder.Property(d => d.UserId).HasMaxLength(128);
        builder.Property(d => d.SourceBoundary).HasMaxLength(128);
        builder.Property(d => d.BusName).HasMaxLength(128);
        builder.Property(d => d.LastError).HasMaxLength(2048);
        builder.Property(d => d.Reason).HasMaxLength(64).IsRequired();

        builder.HasIndex(d => new { d.QueueName, d.DeadLetteredAt })
            .HasDatabaseName("IX___TransportDeadLetters_Queue_At");
    }
}

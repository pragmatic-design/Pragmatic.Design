using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Messaging.Sql.Entities;

/// <summary>EF configuration for the fan-out registry <c>__TransportSubscriptions</c>.</summary>
public sealed class TransportSubscriptionEntityTypeConfiguration : IEntityTypeConfiguration<TransportSubscription>
{
    public const string TableName = "__TransportSubscriptions";

    public void Configure(EntityTypeBuilder<TransportSubscription> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Topic).HasMaxLength(256).IsRequired();
        builder.Property(s => s.SubscriptionName).HasMaxLength(256).IsRequired();

        builder.HasIndex(s => new { s.Topic, s.SubscriptionName })
            .IsUnique()
            .HasDatabaseName("IX___TransportSubscriptions_Topic_Name");
    }
}

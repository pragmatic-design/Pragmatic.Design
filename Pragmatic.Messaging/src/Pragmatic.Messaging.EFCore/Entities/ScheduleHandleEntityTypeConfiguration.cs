using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Messaging.EFCore.Entities;

/// <summary>
///     EF Core entity configuration for <see cref="ScheduleHandleRecord"/>.
///     Table: <c>__ScheduleHandles</c>.
/// </summary>
public sealed class ScheduleHandleEntityTypeConfiguration : IEntityTypeConfiguration<ScheduleHandleRecord>
{
    /// <summary>Default table name for schedule handles.</summary>
    public const string TableName = "__ScheduleHandles";

    public void Configure(EntityTypeBuilder<ScheduleHandleRecord> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(e => e.ScheduleId);

        builder.Property(e => e.Topic)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(e => e.SequenceNumber)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        // Index for retention purges (older-than cleanup)
        builder.HasIndex(e => e.CreatedAt);
    }
}

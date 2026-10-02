using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Audit.EFCore.Entities;

/// <summary>
///     EF Core configuration for <see cref="PrunedRange" />. Table: <c>__AuditPrunedRanges</c>.
/// </summary>
/// <remarks>
///     These rows are what keeps retention from looking like tampering. They must outlive the segments
///     they describe — deleting a pruned range would turn a declared gap back into an unexplained one.
/// </remarks>
public sealed class PrunedRangeEntityTypeConfiguration : IEntityTypeConfiguration<PrunedRange>
{
    /// <summary>Default table name.</summary>
    public const string TableName = "__AuditPrunedRanges";

    public void Configure(EntityTypeBuilder<PrunedRange> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(p => p.FromSegmentId);
        builder.Property(p => p.FromSegmentId).HasMaxLength(32).IsRequired();
        builder.Property(p => p.UntilSegmentId).HasMaxLength(32).IsRequired();
        builder.Property(p => p.PrunedAt).HasConversion<UtcTicksConverter>().IsRequired();
        builder.Property(p => p.LinkHash).IsRequired();
    }
}

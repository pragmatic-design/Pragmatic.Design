using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Audit.EFCore.Entities;

/// <summary>
///     EF Core configuration for <see cref="AuditSegment" />. Table: <c>__AuditSegments</c>.
/// </summary>
public sealed class AuditSegmentEntityTypeConfiguration : IEntityTypeConfiguration<AuditSegment>
{
    /// <summary>Default table name.</summary>
    public const string TableName = "__AuditSegments";

    public void Configure(EntityTypeBuilder<AuditSegment> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(s => s.SegmentId);
        builder.Property(s => s.SegmentId).HasMaxLength(32).IsRequired();

        builder.Property(s => s.OpenedAt).HasConversion<UtcTicksConverter>().IsRequired();
        builder.Property(s => s.SealedAt).HasConversion<NullableUtcTicksConverter>();
        builder.Property(s => s.EntryCount).IsRequired();
        builder.Property(s => s.MerkleRoot);
        builder.Property(s => s.PreviousHash);
        builder.Property(s => s.SegmentHash);

        builder.Ignore(s => s.IsSealed);

        // Verification walks sealed segments in chronological order.
        builder.HasIndex(s => s.SealedAt);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Audit.EFCore.Entities;

/// <summary>
///     EF Core configuration for <see cref="AuditEntry" />. Table: <c>__AuditEntries</c>.
/// </summary>
public sealed class AuditEntryEntityTypeConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <summary>Default table name.</summary>
    public const string TableName = "__AuditEntries";

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable(TableName);

        // Seq is a database identity: a per-segment counter would need coordination between writers,
        // which is exactly the serialisation the segment design exists to avoid. Gaps and out-of-order
        // commits are fine — ordering by Seq within a segment is still deterministic at sealing time.
        builder.HasKey(e => e.Seq);
        builder.Property(e => e.Seq).ValueGeneratedOnAdd();

        builder.Property(e => e.SegmentId).HasMaxLength(32).IsRequired();
        builder.Property(e => e.OccurredAt).HasConversion<UtcTicksConverter>().IsRequired();
        builder.Property(e => e.Category).IsRequired();
        builder.Property(e => e.Operation).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ActorRef).HasMaxLength(128);
        builder.Property(e => e.SubjectRef).HasMaxLength(128);
        builder.Property(e => e.TenantId).HasMaxLength(128);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        // Nullable and unindexed: a change outside any declared operation truthfully has none, and the
        // question this answers ("which operation touched this") is asked of a subject or a row first,
        // through the indexes below.
        builder.Property(e => e.BusinessOperation).HasMaxLength(256);
        // Same width as the other refs: it holds a pseudonym, like ActorRef and SubjectRef.
        builder.Property(e => e.OnBehalfOfRef).HasMaxLength(128);
        builder.Property(e => e.TargetType).HasMaxLength(256);
        builder.Property(e => e.TargetId).HasMaxLength(128);
        builder.Property(e => e.Outcome).IsRequired();
        builder.Property(e => e.ValueHash);
        builder.Property(e => e.Detail).HasMaxLength(2048);

        // Sealing reads a whole segment in order; this index is what keeps that from being a scan.
        builder.HasIndex(e => new { e.SegmentId, e.Seq });

        // "What happened to this subject" is the question the trail exists to answer.
        builder.HasIndex(e => new { e.SubjectRef, e.OccurredAt });
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => e.CorrelationId);
    }
}

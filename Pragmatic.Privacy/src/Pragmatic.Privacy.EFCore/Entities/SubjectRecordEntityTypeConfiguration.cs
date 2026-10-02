using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Privacy.EFCore.Entities;

/// <summary>
///     EF Core configuration for <see cref="SubjectRecord" />. Table: <c>__Subjects</c>.
/// </summary>
public sealed class SubjectRecordEntityTypeConfiguration : IEntityTypeConfiguration<SubjectRecord>
{
    /// <summary>Default table name.</summary>
    public const string TableName = "__Subjects";

    public void Configure(EntityTypeBuilder<SubjectRecord> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(s => s.SubjectRef);
        builder.Property(s => s.SubjectRef).HasMaxLength(64).IsRequired();
        builder.Property(s => s.SubjectType).HasMaxLength(128).IsRequired();
        builder.Property(s => s.LookupIndex);
        builder.Property(s => s.Identifier);

        // Stored as UTC ticks: several providers cannot translate a DateTimeOffset comparison into SQL,
        // which would make every date filter on this table fail at query-compilation time.
        builder.Property(s => s.CreatedAt).HasConversion<UtcTicksConverter>().IsRequired();
        builder.Property(s => s.ForgottenAt).HasConversion<NullableUtcTicksConverter>();

        // Every lookup goes through the blind index, and two live subjects must never share one — that
        // would make "who is this reference" ambiguous. Erased rows keep a null index and drop out of
        // the constraint, which is exactly what lets a returning identity be treated as a new subject.
        builder.HasIndex(s => s.LookupIndex).IsUnique();
    }
}

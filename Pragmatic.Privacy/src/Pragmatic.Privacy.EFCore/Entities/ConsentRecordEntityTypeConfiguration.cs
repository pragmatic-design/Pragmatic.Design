using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Privacy.EFCore.Entities;

/// <summary>
///     EF Core configuration for <see cref="ConsentRecord" />. Table: <c>__Consents</c>.
/// </summary>
public sealed class ConsentRecordEntityTypeConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    /// <summary>Default table name.</summary>
    public const string TableName = "__Consents";

    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable(TableName);

        // Keyed on the notice version too: consenting again under a new notice is a new fact, not an
        // update of the old one. Overwriting would erase what the subject was actually told.
        builder.HasKey(c => new { c.SubjectRef, c.Purpose, c.NoticeVersion });

        builder.Property(c => c.SubjectRef).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Purpose).HasMaxLength(256).IsRequired();
        builder.Property(c => c.NoticeVersion).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Source).HasMaxLength(256);

        builder.Property(c => c.GrantedAt).HasConversion<UtcTicksConverter>().IsRequired();
        builder.Property(c => c.WithdrawnAt).HasConversion<NullableUtcTicksConverter>();

        builder.Ignore(c => c.IsActive);

        // "What has this subject agreed to" is the question both the access response and every
        // processing decision start from.
        builder.HasIndex(c => c.SubjectRef);
    }
}

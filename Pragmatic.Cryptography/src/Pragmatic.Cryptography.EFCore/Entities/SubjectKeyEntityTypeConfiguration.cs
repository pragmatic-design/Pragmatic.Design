using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Cryptography.EFCore.Entities;

/// <summary>
///     EF Core entity configuration for <see cref="SubjectKeyRecord" />.
///     Table: <c>__SubjectKeys</c>.
/// </summary>
public sealed class SubjectKeyEntityTypeConfiguration : IEntityTypeConfiguration<SubjectKeyRecord>
{
    /// <summary>Default table name for subject keys.</summary>
    public const string TableName = "__SubjectKeys";

    public void Configure(EntityTypeBuilder<SubjectKeyRecord> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(e => e.SubjectRef);

        builder.Property(e => e.SubjectRef)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.KeyId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.WrappedKey);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.DestroyedAt);

        // Reads resolve by key id, not by subject: a decrypting reader has the id from the ciphertext
        // header and nothing else. Unique because two subjects sharing a key id would make that lookup
        // ambiguous — and the id is a fingerprint of the material, so a collision means a reused key.
        builder.HasIndex(e => e.KeyId)
            .IsUnique();
    }
}

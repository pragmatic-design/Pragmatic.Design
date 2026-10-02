using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Configuration;

/// <summary>
///     EF Core configuration for <see cref="ExternalIdentityRecord{TKey}"/>.
/// </summary>
public sealed class ExternalIdentityRecordConfiguration<TKey> : IEntityTypeConfiguration<ExternalIdentityRecord<TKey>>
    where TKey : notnull
{
    public void Configure(EntityTypeBuilder<ExternalIdentityRecord<TKey>> builder)
    {
        builder.ToTable("ExternalIdentityRecords");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Issuer)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(e => e.Subject)
            .IsRequired()
            .HasMaxLength(512);

        // ExternalIdentityKey is a computed expression-body property (Issuer|Subject).
        // Lookups use the unique index on (Issuer, Subject) — no persisted column needed.
        builder.Ignore(e => e.ExternalIdentityKey);

        // Unique: one provider+subject combo per user
        builder.HasIndex(e => new { e.Issuer, e.Subject })
            .IsUnique()
            .HasDatabaseName("IX_ExternalIdentityRecords_Issuer_Subject");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_ExternalIdentityRecords_UserId");
    }
}

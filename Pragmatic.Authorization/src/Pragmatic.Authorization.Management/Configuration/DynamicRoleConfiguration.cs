using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Authorization.Management.Entities;

namespace Pragmatic.Authorization.Management.Configuration;

/// <summary>
///     EF Core configuration for <see cref="DynamicRole"/>.
/// </summary>
public sealed class DynamicRoleConfiguration : IEntityTypeConfiguration<DynamicRole>
{
    public void Configure(EntityTypeBuilder<DynamicRole> builder)
    {
        builder.HasIndex(e => new { e.Name, e.TenantId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.TenantId).HasMaxLength(128);
    }
}

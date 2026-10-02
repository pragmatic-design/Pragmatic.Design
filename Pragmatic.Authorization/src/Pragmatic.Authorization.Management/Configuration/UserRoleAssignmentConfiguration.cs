using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Authorization.Management.Entities;

namespace Pragmatic.Authorization.Management.Configuration;

/// <summary>
///     EF Core configuration for <see cref="UserRoleAssignment"/>.
/// </summary>
public sealed class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.HasIndex(e => new { e.UserId, e.RoleName, e.TenantId });

        builder.Property(e => e.UserId).HasMaxLength(256).IsRequired();
        builder.Property(e => e.RoleName).HasMaxLength(256).IsRequired();
        builder.Property(e => e.TenantId).HasMaxLength(128);
        builder.Property(e => e.AssignedBy).HasMaxLength(256);
    }
}

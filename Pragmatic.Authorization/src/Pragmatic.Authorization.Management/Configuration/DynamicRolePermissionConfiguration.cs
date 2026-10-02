using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Authorization.Management.Entities;

namespace Pragmatic.Authorization.Management.Configuration;

/// <summary>
///     EF Core configuration for <see cref="DynamicRolePermission"/>.
/// </summary>
public sealed class DynamicRolePermissionConfiguration : IEntityTypeConfiguration<DynamicRolePermission>
{
    public void Configure(EntityTypeBuilder<DynamicRolePermission> builder)
    {
        // Non-unique: temporal records allow multiple rows for the same (RoleName, PermissionName, TenantId)
        // across different ValidFrom/ValidTo windows. Uniqueness is enforced at the application level
        // by checking for active assignments before inserting (see AssignPermissionsToRole).
        builder.HasIndex(e => new { e.RoleName, e.PermissionName, e.TenantId });

        builder.Property(e => e.RoleName).HasMaxLength(256).IsRequired();
        builder.Property(e => e.PermissionName).HasMaxLength(256).IsRequired();
        builder.Property(e => e.TenantId).HasMaxLength(128);
    }
}

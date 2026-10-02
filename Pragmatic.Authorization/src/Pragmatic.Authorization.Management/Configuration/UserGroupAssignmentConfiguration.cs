using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Authorization.Management.Entities;

namespace Pragmatic.Authorization.Management.Configuration;

/// <summary>
///     EF Core configuration for <see cref="UserGroupAssignment"/>.
/// </summary>
public sealed class UserGroupAssignmentConfiguration : IEntityTypeConfiguration<UserGroupAssignment>
{
    public void Configure(EntityTypeBuilder<UserGroupAssignment> builder)
    {
        builder.HasIndex(e => new { e.UserId, e.GroupName, e.TenantId });

        builder.Property(e => e.UserId).HasMaxLength(256).IsRequired();
        builder.Property(e => e.GroupName).HasMaxLength(256).IsRequired();
        builder.Property(e => e.TenantId).HasMaxLength(128);
        builder.Property(e => e.AssignedBy).HasMaxLength(256);
    }
}

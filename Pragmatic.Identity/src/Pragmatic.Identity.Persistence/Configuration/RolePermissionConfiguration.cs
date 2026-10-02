using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Configuration;

/// <summary>
///     EF Core configuration for <see cref="RolePermission"/>.
/// </summary>
/// <param name="activeRowFilter">
///     Provider-specific <c>WHERE ValidTo IS NULL</c> filter SQL for the active-uniqueness index,
///     or <see langword="null"/> to fall back to a non-filtered index. See <see cref="TemporalIndexFilter"/>.
/// </param>
public sealed class RolePermissionConfiguration(string? activeRowFilter = null)
    : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", t =>
            t.HasCheckConstraint(
                "CK_RolePermissions_ValidRange",
                "ValidTo IS NULL OR ValidTo >= ValidFrom"));
        builder.HasKey(rp => rp.Id);

        builder.Property(rp => rp.RoleName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(rp => rp.PermissionName)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(rp => rp.ValidFrom)
            .IsRequired();

        // Index for temporal queries: role lookup with time range
        builder.HasIndex(rp => new { rp.RoleName, rp.ValidFrom, rp.ValidTo })
            .HasDatabaseName("IX_RolePermissions_Role_Temporal");

        // Disambiguates historical (audited) rows: same role+permission may recur over time.
        builder.HasIndex(rp => new { rp.RoleName, rp.PermissionName, rp.ValidFrom })
            .IsUnique()
            .HasDatabaseName("IX_RolePermissions_Role_Permission_ValidFrom");

        // Active-uniqueness: at most ONE active (ValidTo IS NULL) row per role+permission.
        // A FILTERED unique index expresses this directly. Without a filter a unique index is
        // impossible (it would reject legitimate historical rows), so the fallback is a plain
        // lookup index and the invariant must be enforced at the application layer via
        // TemporalActiveRowExtensions.EnsureNoActiveRolePermissionAsync before inserting an active row.
        var activeIndex = builder.HasIndex(rp => new { rp.RoleName, rp.PermissionName })
            .HasDatabaseName("UX_RolePermissions_Role_Permission_Active");

        if (activeRowFilter is not null)
            activeIndex.IsUnique().HasFilter(activeRowFilter);
    }
}

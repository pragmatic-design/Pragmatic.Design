using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Configuration;

/// <summary>
///     EF Core configuration for <see cref="GroupRole"/>.
/// </summary>
/// <param name="activeRowFilter">
///     Provider-specific <c>WHERE ValidTo IS NULL</c> filter SQL for the active-uniqueness index,
///     or <see langword="null"/> to fall back to a non-filtered index. See <see cref="TemporalIndexFilter"/>.
/// </param>
public sealed class GroupRoleConfiguration(string? activeRowFilter = null)
    : IEntityTypeConfiguration<GroupRole>
{
    public void Configure(EntityTypeBuilder<GroupRole> builder)
    {
        builder.ToTable("GroupRoles", t =>
            t.HasCheckConstraint(
                "CK_GroupRoles_ValidRange",
                "ValidTo IS NULL OR ValidTo >= ValidFrom"));
        builder.HasKey(gr => gr.Id);

        builder.Property(gr => gr.GroupName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(gr => gr.RoleName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(gr => gr.ValidFrom)
            .IsRequired();

        builder.HasIndex(gr => new { gr.GroupName, gr.ValidFrom, gr.ValidTo })
            .HasDatabaseName("IX_GroupRoles_Group_Temporal");

        // Disambiguates historical (audited) rows: same group+role may recur over time.
        builder.HasIndex(gr => new { gr.GroupName, gr.RoleName, gr.ValidFrom })
            .IsUnique()
            .HasDatabaseName("IX_GroupRoles_Group_Role_ValidFrom");

        // Reverse lookup: "which groups grant this role?" (role → groups).
        builder.HasIndex(gr => gr.RoleName)
            .HasDatabaseName("IX_GroupRoles_Role");

        // Active-uniqueness: at most ONE active (ValidTo IS NULL) row per group+role.
        // Filtered unique where the provider supports it; otherwise a plain lookup index and the
        // invariant must be enforced at the application layer via
        // TemporalActiveRowExtensions.EnsureNoActiveGroupRoleAsync before inserting an active row.
        var activeIndex = builder.HasIndex(gr => new { gr.GroupName, gr.RoleName })
            .HasDatabaseName("UX_GroupRoles_Group_Role_Active");

        if (activeRowFilter is not null)
            activeIndex.IsUnique().HasFilter(activeRowFilter);
    }
}

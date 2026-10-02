using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Configuration;

/// <summary>
///     EF Core configuration for <see cref="UserRole{TKey}"/>.
///     Applied through <see cref="IdentityModelBuilderExtensions.ApplyIdentityConfigurations{TUser,TKey}"/>.
/// </summary>
/// <param name="activeRowFilter">
///     Provider-specific <c>WHERE ValidTo IS NULL</c> filter SQL for the active-uniqueness index,
///     or <see langword="null"/> to fall back to a non-filtered index. See <see cref="TemporalIndexFilter"/>.
/// </param>
public sealed class UserRoleConfiguration<TKey>(string? activeRowFilter = null)
    : IEntityTypeConfiguration<UserRole<TKey>>
    where TKey : notnull
{
    public void Configure(EntityTypeBuilder<UserRole<TKey>> builder)
    {
        builder.ToTable("UserRoles", t =>
            t.HasCheckConstraint(
                "CK_UserRoles_ValidRange",
                "ValidTo IS NULL OR ValidTo >= ValidFrom"));
        builder.HasKey(ur => ur.Id);

        builder.Property(ur => ur.RoleName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(ur => ur.AssignedBy)
            .HasMaxLength(256);

        builder.Property(ur => ur.ValidFrom)
            .IsRequired();

        // Disambiguates historical (audited) rows: same user+role may recur over time.
        builder.HasIndex(ur => new { ur.UserId, ur.RoleName, ur.ValidFrom })
            .IsUnique()
            .HasDatabaseName("IX_UserRoles_User_Role_ValidFrom");

        builder.HasIndex(ur => new { ur.UserId, ur.ValidFrom, ur.ValidTo })
            .HasDatabaseName("IX_UserRoles_User_Temporal");

        // Active-uniqueness: at most ONE active (ValidTo IS NULL) assignment per user+role.
        // Filtered unique where the provider supports it; otherwise a plain lookup index and the
        // invariant must be enforced at the application layer via
        // TemporalActiveRowExtensions.EnsureNoActiveUserRoleAsync before inserting an active row.
        var activeIndex = builder.HasIndex(ur => new { ur.UserId, ur.RoleName })
            .HasDatabaseName("UX_UserRoles_User_Role_Active");

        if (activeRowFilter is not null)
            activeIndex.IsUnique().HasFilter(activeRowFilter);
    }
}

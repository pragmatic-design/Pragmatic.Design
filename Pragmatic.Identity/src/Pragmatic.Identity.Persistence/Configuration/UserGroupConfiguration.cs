using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Configuration;

/// <summary>
///     EF Core configuration for <see cref="UserGroup{TKey}"/>.
/// </summary>
/// <param name="activeRowFilter">
///     Provider-specific <c>WHERE ValidTo IS NULL</c> filter SQL for the active-uniqueness index,
///     or <see langword="null"/> to fall back to a non-filtered index. See <see cref="TemporalIndexFilter"/>.
/// </param>
public sealed class UserGroupConfiguration<TKey>(string? activeRowFilter = null)
    : IEntityTypeConfiguration<UserGroup<TKey>>
    where TKey : notnull
{
    public void Configure(EntityTypeBuilder<UserGroup<TKey>> builder)
    {
        builder.ToTable("UserGroups", t =>
            t.HasCheckConstraint(
                "CK_UserGroups_ValidRange",
                "ValidTo IS NULL OR ValidTo >= ValidFrom"));
        builder.HasKey(ug => ug.Id);

        builder.Property(ug => ug.GroupName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(ug => ug.AssignedBy)
            .HasMaxLength(256);

        builder.Property(ug => ug.ValidFrom)
            .IsRequired();

        // Disambiguates historical (audited) memberships: same user+group may recur over time.
        builder.HasIndex(ug => new { ug.UserId, ug.GroupName, ug.ValidFrom })
            .IsUnique()
            .HasDatabaseName("IX_UserGroups_User_Group_ValidFrom");

        builder.HasIndex(ug => new { ug.UserId, ug.ValidFrom, ug.ValidTo })
            .HasDatabaseName("IX_UserGroups_User_Temporal");

        // Active-uniqueness: at most ONE active (ValidTo IS NULL) membership per user+group.
        // Filtered unique where the provider supports it; otherwise a plain lookup index and the
        // invariant must be enforced at the application layer via
        // TemporalActiveRowExtensions.EnsureNoActiveUserGroupAsync before inserting an active row.
        var activeIndex = builder.HasIndex(ug => new { ug.UserId, ug.GroupName })
            .HasDatabaseName("UX_UserGroups_User_Group_Active");

        if (activeRowFilter is not null)
            activeIndex.IsUnique().HasFilter(activeRowFilter);
    }
}

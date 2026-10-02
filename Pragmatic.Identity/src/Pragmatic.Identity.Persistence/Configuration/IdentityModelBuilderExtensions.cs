using Microsoft.EntityFrameworkCore;
using Pragmatic.Identity.Persistence.Entities;

namespace Pragmatic.Identity.Persistence.Configuration;

/// <summary>
///     Extension methods for applying Identity entity configurations to any <see cref="ModelBuilder"/>.
/// </summary>
public static class IdentityModelBuilderExtensions
{
    /// <summary>
    ///     Applies all Identity entity configurations to a boundary DbContext.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="providerName">
    ///     The active EF Core provider name (pass <c>Database.ProviderName</c> from
    ///     <c>OnModelCreating</c>). Drives the provider-specific filter SQL for the
    ///     active-uniqueness partial indexes on temporal tables. When <see langword="null"/>
    ///     (or a provider that cannot express partial indexes) those indexes fall back to a
    ///     plain, non-unique lookup index. See <see cref="TemporalIndexFilter"/>.
    /// </param>
    public static ModelBuilder ApplyIdentityConfigurations<TUser, TKey>(
        this ModelBuilder modelBuilder,
        string? providerName = null)
        where TUser : class
        where TKey : notnull
    {
        var activeFilter = TemporalIndexFilter.ActiveRowFilter(providerName);

        modelBuilder.ApplyConfiguration(new UserRoleConfiguration<TKey>(activeFilter));
        modelBuilder.ApplyConfiguration(new UserGroupConfiguration<TKey>(activeFilter));
        modelBuilder.ApplyConfiguration(new RolePermissionConfiguration(activeFilter));
        modelBuilder.ApplyConfiguration(new GroupRoleConfiguration(activeFilter));
        modelBuilder.ApplyConfiguration(new ExternalIdentityRecordConfiguration<TKey>());

        return modelBuilder;
    }
}

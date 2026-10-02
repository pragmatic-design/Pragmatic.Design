using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Identity.Persistence.Configuration;

/// <summary>
///     Fluent builder for configuring Identity persistence services:
///     EF temporal stores and external identity support.
/// </summary>
public sealed class IdentityPersistenceBuilder
{
    internal IdentityPersistenceBuilder(IServiceCollection services)
    {
        Services = services;
    }

    internal IServiceCollection Services { get; }

    /// <summary>
    ///     Whether the EF temporal role permission store should be registered.
    /// </summary>
    internal bool UseEfRoleStore { get; private set; } = true;

    /// <summary>
    ///     Whether the EF temporal group role store should be registered.
    /// </summary>
    internal bool UseEfGroupStore { get; private set; } = true;

    /// <summary>
    ///     Disables the default EF-backed role permission store.
    ///     Use this when a custom <c>IRolePermissionStore</c> is already registered
    ///     via <c>AuthorizationBuilder.UseRolePermissionStore&lt;T&gt;()</c>.
    /// </summary>
    public IdentityPersistenceBuilder SkipRolePermissionStore()
    {
        UseEfRoleStore = false;
        return this;
    }

    /// <summary>
    ///     Disables the default EF-backed group role store.
    ///     Use this when a custom <c>IGroupRoleStore</c> is already registered
    ///     via <c>AuthorizationBuilder.UseGroupRoleStore&lt;T&gt;()</c>.
    /// </summary>
    public IdentityPersistenceBuilder SkipGroupRoleStore()
    {
        UseEfGroupStore = false;
        return this;
    }
}

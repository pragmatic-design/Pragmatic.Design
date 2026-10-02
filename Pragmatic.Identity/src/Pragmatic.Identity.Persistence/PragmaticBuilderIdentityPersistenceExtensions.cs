using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity.Persistence.Configuration;
using Pragmatic.Identity.Persistence.Stores;

namespace Pragmatic.Identity.Persistence;

/// <summary>
///     Registers the EF-backed Identity stores.
/// </summary>
/// <remarks>
///     The user shape comes from <c>[PragmaticUser]</c> with an owned identity record, and the
///     persistence topology from <c>[UsePackage&lt;LocalIdentityPackage&gt;]</c>, so nothing here needs
///     to know the user type.
/// </remarks>
public static class PragmaticBuilderIdentityPersistenceExtensions
{
    /// <summary>
    ///     Registers the EF temporal role- and group-permission stores, replacing the in-memory ones
    ///     Authorization registers by default.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Opts individual stores out.</param>
    public static IServiceCollection AddPragmaticIdentityPersistence(
        this IServiceCollection services,
        Action<IdentityPersistenceBuilder>? configure = null)
    {
        var persistenceBuilder = new IdentityPersistenceBuilder(services);
        configure?.Invoke(persistenceBuilder);

        if (persistenceBuilder.UseEfRoleStore)
            services.AddScoped<IRolePermissionStore, EfRolePermissionStore>();

        if (persistenceBuilder.UseEfGroupStore)
            services.AddScoped<IGroupRoleStore, EfGroupRoleStore>();

        return services;
    }
}

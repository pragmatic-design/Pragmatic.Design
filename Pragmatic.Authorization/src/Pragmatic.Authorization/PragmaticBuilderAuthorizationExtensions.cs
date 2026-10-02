using Microsoft.Extensions.Options;
using Pragmatic.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Composition.Extensions;
using Pragmatic.Authorization.Catalog;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Providers;
using Pragmatic.Authorization.Stores;
using Pragmatic.Composition;

namespace Pragmatic.Authorization;

/// <summary>
///     Extension methods for configuring authorization on <see cref="IPragmaticBuilder"/>.
/// </summary>
public static class PragmaticBuilderAuthorizationExtensions
{
    /// <summary>
    ///     Configures the Pragmatic authorization system with permission providers, role mappings, and stores.
    /// </summary>
    public static IPragmaticBuilder UseAuthorization(
        this IPragmaticBuilder builder,
        Action<AuthorizationBuilder>? configure = null)
    {
        builder.Services.AddPragmaticAuthorization(configure);
        return builder;
    }

    /// <summary>
    ///     Registers authorization services on <see cref="IServiceCollection"/>.
    /// </summary>
    public static IServiceCollection AddPragmaticAuthorization(
        this IServiceCollection services,
        Action<AuthorizationBuilder>? configure = null)
    {
        var authBuilder = new AuthorizationBuilder(services);
        configure?.Invoke(authBuilder);

        // Server-side resolution by default: only trust baked "permission" claims when explicitly
        // opted in. When TrustPermissionClaims is false the ClaimsPermissionProvider is NOT registered,
        // so a spoofed/remapped "permission" claim injects no authority — permissions come solely from
        // role/group expansion against the stores. Roles/groups still flow from the signed token.
        if (authBuilder.Options.TrustPermissionClaims)
            services.AddScoped<IPermissionProvider, ClaimsPermissionProvider>();

        // Role store: custom overrides in-memory
        if (authBuilder is { HasCustomRoleStore: false, InMemoryStore: not null })
        {
            services.AddSingleton<IRolePermissionStore>(authBuilder.InMemoryStore);
            services.AddScoped<IPermissionProvider, RoleExpansionProvider>();
        }
        else if (authBuilder.HasCustomRoleStore)
        {
            // Custom store already registered via UseRolePermissionStore — add expansion provider
            services.AddScoped<IPermissionProvider, RoleExpansionProvider>();
        }

        // Group store: custom overrides in-memory
        if (authBuilder is { HasCustomGroupStore: false, InMemoryGroupStore: not null })
        {
            services.AddSingleton<IGroupRoleStore>(authBuilder.InMemoryGroupStore);
        }

        // Group expansion provider — only if any group store is registered
        if (authBuilder.HasCustomGroupStore || authBuilder.InMemoryGroupStore is not null)
        {
            // GroupExpansionProvider needs IRolePermissionStore: registering groups
            // without a role store would silently resolve every group to zero
            // permissions, which looks like an authorization bug at runtime. Fail at
            // startup with an actionable error instead.
            if (authBuilder is { HasCustomRoleStore: false, InMemoryStore: null })
            {
                throw new InvalidOperationException(
                    "Authorization configuration error: a group store is registered " +
                    "(via UseGroupStore() or AddGroup()), but no role-permission store " +
                    "is configured. GroupExpansionProvider cannot resolve group → role " +
                    "→ permission without one — every group lookup would silently " +
                    "return an empty set. Call UseRolePermissionStore<T>() or define " +
                    "in-memory roles via AddRole() before AddPragmaticAuthorization().");
            }

            services.AddScoped<IPermissionProvider, GroupExpansionProvider>();
        }

        // Configure options
        services.Configure<AuthorizationOptions>(o =>
        {
            o.EnablePermissionCaching = authBuilder.Options.EnablePermissionCaching;
            o.TrustPermissionClaims = authBuilder.Options.TrustPermissionClaims;
            o.CacheOptions = authBuilder.Options.CacheOptions;
        });

        // Register the resolver as IUserAuthorization (scoped per request). Built by hand rather than
        // by type so the cache stack comes from CacheCategories.Permissions: type registration would
        // inject the default unkeyed stack, and configuring that category would go on doing nothing.
        services.AddScoped<IUserAuthorization>(sp => new CachedPermissionResolver(
            sp.GetRequiredService<IEnumerable<IPermissionProvider>>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetService<IOptions<AuthorizationOptions>>(),
            Evaluation.PermissionCacheStack.Resolve(sp),
            // GetService, not GetRequiredService: an application without multi-tenancy registers no
            // ITenantContext, and the resolver falls back to the tenant claim. ⚠️ Passing it is what
            // partitions the permission cache by the tenant the request is served for — leaving it
            // out gave every workspace one shared entry per person.
            sp.GetService<MultiTenancy.ITenantContext>()));

        // Delegation: compose subject and actor authority. A decorator rather than a change to the
        // resolver, so every [RequirePermission], permission-based filter and ResourcePolicy inherits
        // it without knowing. Registered unconditionally — with no delegation on the session it
        // forwards untouched, and making it conditional would mean an application that starts using
        // delegation later gets the composition only if it also remembers to turn something on.
        services.Decorate<IUserAuthorization, Delegation.DelegatedUserAuthorization>();
        services.TryAddScoped<Delegation.IActorAuthorityResolver, Delegation.ClaimsActorAuthorityResolver>();

        // ActAs, for code with no HTTP request behind it. The decorator on ICurrentUser is what
        // makes the AsyncLocal scope real — TenantScope shipped without its reader once, and the
        // documented background-job story was inert until someone noticed.
        services.TryAddScoped<Delegation.IDelegationService, Delegation.DelegationService>();
        services.Decorate<ICurrentUser, Delegation.AmbientDelegatedUser>();

        // Scope resolver for [HasOwner]/[HasAccessScopes] data filters: the SG-generated
        // filters take IUserScopeResolver from DI, so leaving it unregistered turns every
        // scoped-entity query into an InvalidOperationException at runtime. TryAdd so a
        // custom resolver registered before this call wins.
        services.TryAddScoped<IUserScopeResolver, Scopes.DefaultUserScopeResolver>();

        // Register catalogs (use TryAdd so SG-generated registrations take precedence). Scoped: the
        // dynamic stores it merges read the request's tenant, and a singleton would capture the first
        // scope's stores for the life of the process.
        services.TryAddScoped<IPermissionCatalog, DefaultPermissionCatalog>();
        services.TryAddSingleton<IResourceCatalog, DefaultResourceCatalog>();

        return services;
    }
}

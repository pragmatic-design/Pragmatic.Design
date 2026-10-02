using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Configuration;

/// <summary>
///     Fluent builder for configuring the Pragmatic authorization system.
/// </summary>
public sealed class AuthorizationBuilder
{
    internal IServiceCollection Services { get; }
    internal AuthorizationOptions Options { get; } = new();
    internal InMemoryRolePermissionStore? InMemoryStore { get; private set; }
    internal InMemoryGroupRoleStore? InMemoryGroupStore { get; private set; }
    internal bool HasCustomRoleStore { get; private set; }
    internal bool HasCustomGroupStore { get; private set; }

    internal AuthorizationBuilder(IServiceCollection services) => Services = services;

    /// <summary>
    ///     When <c>true</c>, baked <c>permission</c> claims on the caller's token/principal are trusted and
    ///     granted directly (registers <see cref="Providers.ClaimsPermissionProvider"/>). Default: <c>false</c>
    ///     — permissions are resolved server-side by expanding <c>role</c>/<c>group</c> claims. See
    ///     <see cref="AuthorizationOptions.TrustPermissionClaims"/> for the full revocation trade-off.
    /// </summary>
    public bool TrustPermissionClaims
    {
        get => Options.TrustPermissionClaims;
        set => Options.TrustPermissionClaims = value;
    }

    /// <summary>Adds a custom permission provider to the chain.</summary>
    public AuthorizationBuilder AddPermissionProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IPermissionProvider
    {
        Services.AddScoped<IPermissionProvider, T>();
        return this;
    }

    /// <summary>Maps a strongly-typed role with its default permissions.</summary>
    public AuthorizationBuilder MapRole<TRole>() where TRole : IRole
    {
        EnsureInMemoryStore();
        GetRequiredInMemoryStore().AddRole(TRole.Name, TRole.DefaultPermissions);
        return this;
    }

    /// <summary>
    ///     Maps a strongly-typed role with its default permissions plus additional
    ///     permissions configured inline. Enables cross-module permission composition.
    /// </summary>
    /// <example>
    ///     <code>
    ///     authz.MapRole&lt;BookingManager&gt;(r => r
    ///         .WithPermissions("identity.login", "accounts.view-profile"));
    ///     </code>
    /// </example>
    public AuthorizationBuilder MapRole<TRole>(Action<RoleBuilder> configure) where TRole : IRole
    {
        EnsureInMemoryStore();
        var rb = new RoleBuilder();
        configure(rb);
        GetRequiredInMemoryStore().AddRole(TRole.Name, rb.Resolve(TRole.DefaultPermissions));
        return this;
    }

    /// <summary>Maps a role by name with inline permission configuration.</summary>
    public AuthorizationBuilder MapRole(string name, Action<RoleBuilder> configure)
    {
        EnsureInMemoryStore();
        var rb = new RoleBuilder();
        configure(rb);
        GetRequiredInMemoryStore().AddRole(name, rb.Resolve());
        return this;
    }

    /// <summary>
    ///     Registers a custom <see cref="IRolePermissionStore"/> implementation.
    ///     Overrides the in-memory store created by <see cref="MapRole"/>.
    /// </summary>
    public AuthorizationBuilder UseRolePermissionStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IRolePermissionStore
    {
        Services.AddScoped<IRolePermissionStore, T>();
        HasCustomRoleStore = true;
        return this;
    }

    /// <summary>
    ///     Registers a custom <see cref="IGroupRoleStore"/> implementation.
    ///     Overrides the in-memory store created by <see cref="MapGroup"/>.
    /// </summary>
    public AuthorizationBuilder UseGroupRoleStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IGroupRoleStore
    {
        Services.AddScoped<IGroupRoleStore, T>();
        HasCustomGroupStore = true;
        return this;
    }

    /// <summary>Maps a strongly-typed group with its default roles.</summary>
    public AuthorizationBuilder MapGroup<TGroup>() where TGroup : IGroup
    {
        EnsureInMemoryGroupStore();
        GetRequiredInMemoryGroupStore().AddGroup(TGroup.Name, TGroup.DefaultRoles);
        return this;
    }

    /// <summary>Maps a strongly-typed group with its default roles plus additional roles configured inline.</summary>
    public AuthorizationBuilder MapGroup<TGroup>(Action<GroupBuilder> configure) where TGroup : IGroup
    {
        EnsureInMemoryGroupStore();
        var gb = new GroupBuilder();
        configure(gb);
        var allRoles = new List<string>(TGroup.DefaultRoles);
        allRoles.AddRange(gb.Roles);
        GetRequiredInMemoryGroupStore().AddGroup(TGroup.Name, allRoles);
        return this;
    }

    /// <summary>Maps a group by name with inline role configuration.</summary>
    public AuthorizationBuilder MapGroup(string name, Action<GroupBuilder> configure)
    {
        EnsureInMemoryGroupStore();
        var gb = new GroupBuilder();
        configure(gb);
        GetRequiredInMemoryGroupStore().AddGroup(name, gb.Roles);
        return this;
    }

    /// <summary>
    ///     Registers an <see cref="IResourceAuthorizer{TResource}"/> implementation for a specific resource type.
    ///     Prefer this overload — it uses no reflection.
    /// </summary>
    public AuthorizationBuilder AddResourceAuthorizer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAuthorizer,
        TResource>()
        where TAuthorizer : class, IResourceAuthorizer<TResource>
    {
        Services.AddScoped<IResourceAuthorizer<TResource>, TAuthorizer>();
        ResourceCatalog.Add(typeof(TResource));
        return this;
    }

    private ResourceAuthorizerCatalog? _resourceCatalog;

    /// <summary>
    ///     The catalog of resource types covered by a registered authorizer, created on first use and
    ///     registered as a singleton. It is what lets the action pipeline distinguish an action nobody
    ///     meant to protect from one whose base type has an authorizer the container will never return
    ///     for it — see <see cref="IResourceAuthorizerCatalog" />.
    /// </summary>
    private ResourceAuthorizerCatalog ResourceCatalog
    {
        get
        {
            if (_resourceCatalog is null)
            {
                _resourceCatalog = new ResourceAuthorizerCatalog();
                Services.AddSingleton<IResourceAuthorizerCatalog>(_resourceCatalog);
            }

            return _resourceCatalog;
        }
    }

    /// <summary>Enables cross-request permission caching with the specified expiration.</summary>
    public AuthorizationBuilder UsePermissionCache(TimeSpan expiration)
    {
        Options.CacheOptions = new PermissionCacheOptions { Expiration = expiration };
        RegisterCacheInvalidator();
        return this;
    }

    /// <summary>Enables cross-request permission caching with detailed options.</summary>
    public AuthorizationBuilder UsePermissionCache(Action<PermissionCacheOptions> configure)
    {
        var cacheOptions = new PermissionCacheOptions();
        configure(cacheOptions);
        Options.CacheOptions = cacheOptions;
        RegisterCacheInvalidator();
        return this;
    }

    /// <summary>
    ///     Registers <see cref="IPermissionCacheInvalidator"/> so the application can
    ///     evict cached permissions on demand (the <see cref="PermissionCacheStrategy.ManualInvalidation"/>
    ///     strategy). Idempotent — safe to call from either <c>UsePermissionCache</c> overload.
    /// </summary>
    private void RegisterCacheInvalidator()
        // Factory registration: IGroupRoleStore is optional (only registered when a group store is
        // configured). GetService returns null otherwise — MS DI cannot satisfy an optional ctor
        // parameter by type, so resolve it explicitly here. When present, InvalidateRoleAsync uses it
        // to reach users who hold the role transitively via a group.
        => Services.TryAddScoped<IPermissionCacheInvalidator>(sp =>
            new PermissionCacheInvalidator(
                Evaluation.PermissionCacheStack.Resolve(sp)!,
                sp.GetService<IGroupRoleStore>()));


    private void EnsureInMemoryStore()
    {
        InMemoryStore ??= new InMemoryRolePermissionStore();
    }

    private void EnsureInMemoryGroupStore()
    {
        InMemoryGroupStore ??= new InMemoryGroupRoleStore();
    }

    private InMemoryRolePermissionStore GetRequiredInMemoryStore()
    {
        return InMemoryStore
            ?? throw new InvalidOperationException(
                "InMemoryRolePermissionStore is not initialized. Call EnsureInMemoryStore() first.");
    }

    private InMemoryGroupRoleStore GetRequiredInMemoryGroupStore()
    {
        return InMemoryGroupStore
            ?? throw new InvalidOperationException(
                "InMemoryGroupRoleStore is not initialized. Call EnsureInMemoryGroupStore() first.");
    }
}

/// <summary>
///     Fluent builder for inline role configuration.
///     Supports additive, subtractive, and replacement operations.
/// </summary>
public sealed class RoleBuilder
{
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private bool _cleared;

    internal List<string> Permissions { get; } = [];

    /// <summary>Adds permissions to this role.</summary>
    public RoleBuilder WithPermissions(params string[] permissions)
    {
        Permissions.AddRange(permissions);
        return this;
    }

    /// <summary>
    ///     Includes all permissions from a module-defined <see cref="IRoleDefinition"/>.
    ///     Combines multiple definitions into a single application role.
    /// </summary>
    /// <typeparam name="TDefinition">The role definition type.</typeparam>
    public RoleBuilder IncludeDefinition<TDefinition>() where TDefinition : IRoleDefinition
    {
        Permissions.AddRange(TDefinition.Permissions);
        return this;
    }

    /// <summary>
    ///     Grants all permissions in the system (wildcard <c>*</c>).
    ///     Equivalent to <c>WithPermissions("*")</c>.
    /// </summary>
    public RoleBuilder WithAllPermissions()
    {
        Permissions.Add("*");
        return this;
    }

    /// <summary>
    ///     Grants all permissions for a specific boundary (wildcard <c>{boundary}.*</c>).
    ///     The boundary slug is derived from the type name by stripping the "Boundary" suffix and lowercasing.
    /// </summary>
    /// <typeparam name="TBoundary">The boundary type (e.g., <c>BookingBoundary</c>).</typeparam>
    public RoleBuilder WithAllPermissions<TBoundary>()
    {
        var slug = DeriveBoundarySlug(typeof(TBoundary).Name);
        Permissions.Add($"{slug}.*");
        return this;
    }

    private static string DeriveBoundarySlug(string boundaryTypeName)
    {
        const string suffix = "Boundary";
        var name = boundaryTypeName.EndsWith(suffix, StringComparison.Ordinal)
            ? boundaryTypeName[..^suffix.Length]
            : boundaryTypeName;
        return name.ToLowerInvariant();
    }

    /// <summary>
    ///     Grants a specific operation across all entities in a boundary.
    ///     Example: <c>WithOperation&lt;BookingBoundary&gt;(CrudOperation.Read)</c> → <c>"booking.*.read"</c>
    /// </summary>
    public RoleBuilder WithOperation<TBoundary>(CrudOperation operation)
    {
        var slug = DeriveBoundarySlug(typeof(TBoundary).Name);
        var op = operation.ToString().ToLowerInvariant();
        Permissions.Add($"{slug}.*.{op}");
        return this;
    }

    /// <summary>Removes specific permissions (including defaults from <see cref="IRole"/>).</summary>
    public RoleBuilder WithoutPermissions(params string[] permissions)
    {
        foreach (var p in permissions)
            _excluded.Add(p);
        return this;
    }

    /// <summary>
    ///     Clears all default permissions from <see cref="IRole.DefaultPermissions"/>.
    ///     Only permissions added via <see cref="WithPermissions"/> after this call will apply.
    /// </summary>
    public RoleBuilder ClearDefaults()
    {
        _cleared = true;
        return this;
    }

    /// <summary>Resolves the final permission set after applying excludes and clears.</summary>
    internal IEnumerable<string> Resolve(IReadOnlyList<string>? defaults = null)
    {
        var result = new List<string>();

        if (!_cleared && defaults is not null)
            result.AddRange(defaults);

        result.AddRange(Permissions);

        if (_excluded.Count > 0)
        {
            // Fail-fast: detect exclusions that conflict with wildcard grants.
            // "booking.*" covers "booking.reservation.delete" — exact-match exclusion would silently fail.
            foreach (var excluded in _excluded)
            {
                var conflicting = result.FirstOrDefault(p => p.Contains('*') && WildcardMatcher.Matches(p, excluded));
                if (conflicting is not null)
                    throw new InvalidOperationException(
                        $"Cannot exclude '{excluded}' — covered by wildcard '{conflicting}'. " +
                        "Use explicit permissions instead of wildcards when using WithoutPermissions.");
            }

            // Remove exact matches AND any wildcard exclusion patterns that cover a granted permission.
            return result.Where(p => !_excluded.Contains(p)
                && !_excluded.Any(ex => ex.Contains('*') && WildcardMatcher.Matches(ex, p)));
        }

        return result;
    }
}

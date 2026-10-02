using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.MultiTenancy.Resolvers;

namespace Pragmatic.MultiTenancy;

/// <summary>
///     Fluent builder for configuring multi-tenancy resolution strategies.
/// </summary>
/// <remarks>
///     <para>
///         <b>Several strategies chain, in call order.</b> Each <c>Use*</c> adds a strategy; the tenant
///         is the first non-empty answer. <c>UseHeader().UseClaim().UseSingleTenant("demo")</c> reads the
///         header, then the claim, and falls back to <c>demo</c>. One strategy is registered as itself;
///         several are wrapped in a <see cref="CompositeTenantResolver" />.
///     </para>
///     <para>
///         ⚠️ No <c>Use*</c> registers itself as <em>the</em> <see cref="ITenantResolver" />: if each
///         did, the last call would replace the others and the chain would read only its last link —
///         with a fixed tenant last, every request would be that tenant.
///     </para>
///     <para>
///         <b>One configuration wins.</b> The resolver is registered once, when the configuration ends,
///         and it replaces any <see cref="ITenantResolver" /> registered before — so the application's
///         <c>UseMultiTenancy(...)</c> replaces the single-tenant default the generated host registers
///         first, rather than chaining after it. A configuration that adds no strategy leaves the
///         existing resolver alone.
///     </para>
/// </remarks>
public sealed class MultiTenancyBuilder(IServiceCollection services)
{
    private readonly List<Strategy> _strategies = [];

    /// <summary>
    ///     The underlying service collection.
    /// </summary>
    public IServiceCollection Services => services;

    /// <summary>
    ///     Uses a fixed tenant ID. Default for single-tenant applications (zero overhead). After other
    ///     strategies it is the fallback for a request none of them resolves.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tenantId"/> is null, empty or whitespace.</exception>
    public MultiTenancyBuilder UseSingleTenant(string tenantId = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var resolver = new SingleTenantResolver(tenantId);
        _strategies.Add(new Strategy(ServiceDescriptor.Singleton<ITenantResolver>(resolver), _ => resolver));
        return this;
    }

    /// <summary>
    ///     Resolves tenant from an HTTP header.
    ///     Default header: <c>X-Tenant-Id</c>.
    /// </summary>
    public MultiTenancyBuilder UseHeader(string? headerName = null)
    {
        if (headerName is not null)
            services.Configure<MultiTenancyOptions>(o => o.TenantHeaderName = headerName);

        return Add<HeaderTenantResolver>();
    }

    /// <summary>
    ///     Resolves tenant from a JWT claim.
    ///     Default claim type: <c>tenant_id</c>.
    /// </summary>
    public MultiTenancyBuilder UseClaim(string? claimType = null)
    {
        if (claimType is not null)
            services.Configure<MultiTenancyOptions>(o => o.TenantClaimType = claimType);

        return Add<ClaimTenantResolver>();
    }

    /// <summary>
    ///     Resolves tenant from the request's subdomain (e.g., <c>acme.app.com</c> resolves to <c>acme</c>).
    /// </summary>
    public MultiTenancyBuilder UseSubdomain() => Add<SubdomainTenantResolver>();

    /// <summary>
    ///     Resolves tenant from a route parameter.
    ///     Default parameter: <c>tenantId</c>.
    /// </summary>
    public MultiTenancyBuilder UseRoute(string? parameterName = null)
    {
        if (parameterName is not null)
            services.Configure<MultiTenancyOptions>(o => o.TenantRouteParameter = parameterName);

        return Add<RouteTenantResolver>();
    }

    /// <summary>
    ///     Adds a custom <see cref="ITenantResolver" /> implementation to the chain.
    /// </summary>
    public MultiTenancyBuilder UseResolver<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>() where TResolver : class, ITenantResolver
        => Add<TResolver>();

    /// <summary>
    ///     Registers the configured strategies as the one <see cref="ITenantResolver" />: the strategy
    ///     itself when there is one, a <see cref="CompositeTenantResolver" /> over them in call order
    ///     when there are several.
    /// </summary>
    internal void Register()
    {
        if (_strategies.Count == 0)
            return;

        services.RemoveAll<ITenantResolver>();

        if (_strategies.Count == 1)
        {
            services.Add(_strategies[0].AsTheResolver);
            return;
        }

        var chain = _strategies.ToArray();
        services.AddScoped<ITenantResolver>(sp => new CompositeTenantResolver(
            [.. chain.Select(s => s.FromProvider(sp))],
            sp.GetRequiredService<ILogger<CompositeTenantResolver>>()));
    }

    private MultiTenancyBuilder Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>()
        where TResolver : class, ITenantResolver
    {
        // Registered as itself too, so a chain can take it from the scope with its dependencies.
        services.TryAddScoped<TResolver>();
        _strategies.Add(new Strategy(
            ServiceDescriptor.Scoped<ITenantResolver, TResolver>(),
            sp => sp.GetRequiredService<TResolver>()));
        return this;
    }

    /// <summary>One strategy: how it is registered alone, and how a chain obtains it.</summary>
    private sealed record Strategy(ServiceDescriptor AsTheResolver, Func<IServiceProvider, ITenantResolver> FromProvider);
}

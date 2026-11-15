using Pragmatic.MultiTenancy;

namespace Pragmatic.FeatureFlags;

/// <summary>
///     The evaluation context of a multi-tenant application: the request's tenant, so a tenant targeting rule
///     (<see cref="FeatureFlagRule.Tenant" />) turns a flag on for the tenants it names.
/// </summary>
/// <remarks>
///     <para>
///         <c>AddPragmaticFeatureFlags</c> registers it when no other <see cref="IFeatureFlagContextProvider" />
///         is: an application that combines the tenant with the user, the plan or the environment registers its
///         own, and that one is used.
///     </para>
///     <para>
///         With no <see cref="ITenantContext" /> registered, or no tenant resolved, the context has no tenant —
///         what <see cref="FeatureFlagContext.Empty" /> already said — so an application without multi-tenancy
///         evaluates exactly as before.
///     </para>
/// </remarks>
public sealed class TenantFeatureFlagContextProvider(ITenantContext? tenant = null) : IFeatureFlagContextProvider
{
    /// <inheritdoc />
    public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
        => Task.FromResult(tenant is { IsResolved: true, TenantId: { Length: > 0 } id }
            ? FeatureFlagContext.Empty with { TenantId = id }
            : FeatureFlagContext.Empty);
}

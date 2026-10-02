namespace Pragmatic.MultiTenancy;

/// <summary>
///     Provides access to the current tenant's identity within the request scope.
///     Inject as a scoped service to access tenant information anywhere in the pipeline.
/// </summary>
/// <remarks>
///     <para>
///     Implementations should be registered as scoped services, resolved per-request
///     after tenant resolution middleware has run.
///     </para>
///     <para>
///     For scenarios where no tenant is resolved, use <see cref="UnresolvedTenantContext.Instance" />.
///     </para>
///     <para>
///     This contract is consumed by:
///     <list type="bullet">
///         <item><description>Persistence — TenantFilter for global query filtering</description></item>
///         <item><description>Persistence — TenantConnectionStringProvider for database-per-tenant</description></item>
///         <item><description>Caching — tenant-scoped cache keys and tag invalidation</description></item>
///     </list>
///     </para>
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface ITenantContext
{
    /// <summary>
    ///     Unique identifier for the current tenant. Null when no tenant is resolved.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    ///     Human-readable name of the current tenant. Null when no tenant is resolved.
    /// </summary>
    string? TenantName { get; }

    /// <summary>
    ///     Whether a tenant has been successfully resolved for the current request.
    /// </summary>
    bool IsResolved { get; }
}

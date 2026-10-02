namespace Pragmatic.MultiTenancy;

/// <summary>
///     Marker interface for entities that belong to a specific tenant.
///     Used as a constraint for <c>TenantFilter&lt;T&gt;</c> in row-level tenant isolation.
/// </summary>
/// <remarks>
///     <para>
///     Prefer database-per-tenant isolation over row-level filtering when possible.
///     Row-level filtering (via <see cref="ITenantEntity" />) is appropriate for:
///     <list type="bullet">
///         <item><description>Shared databases with tenant isolation requirements</description></item>
///         <item><description>Small-scale multi-tenancy where separate databases are impractical</description></item>
///     </list>
///     </para>
///     <para>
///     The <c>TenantId</c> property is automatically set on insert by the tenant-aware
///     persistence layer and filtered on read via global query filters.
///     </para>
/// </remarks>
public interface ITenantEntity
{
    /// <summary>
    ///     The tenant this entity belongs to.
    ///     <para>
    ///         Set automatically on insert by <c>TenantInterceptor</c> from the ambient
    ///         <c>ITenantContext</c>. Application code must not overwrite this value after
    ///         the entity has been persisted — doing so bypasses tenant isolation.
    ///         The setter is public because EF Core's interceptor mutates the entity
    ///         outside an object-initializer context; treat it as internal infrastructure.
    ///     </para>
    /// </summary>
    string TenantId { get; set; }
}

namespace Pragmatic.MultiTenancy;

/// <summary>
///     Endpoint metadata saying this route runs in no tenant: tenant resolution refuses a request that resolves one, and publishes none
///     downstream (#724).
/// </summary>
/// <remarks>
///     <para>
///         The stronger sibling of <see cref="TenantAgnosticEndpoint" />. That one says a tenant is not <em>required</em>, and still resolves
///         and publishes one the caller supplies. A platform operation (creating a tenant, a cross-tenant report, an operator's maintenance)
///         must not run inside the tenant a caller brought: its queries would be filtered to that tenant and its writes stamped with it. So
///         a request that resolves a tenant here is answered 400 <c>TENANT_NOT_ALLOWED</c>, and one that does not runs with no tenant.
///     </para>
///     <para>
///         In Abstractions for the same reason as its sibling: a package marks its endpoint without referencing multi-tenancy.
///     </para>
/// </remarks>
public sealed class NoTenantEndpoint
{
    /// <summary>The single instance to attach; the type carries the meaning, not any state.</summary>
    public static readonly NoTenantEndpoint Instance = new();

    private NoTenantEndpoint()
    {
    }
}

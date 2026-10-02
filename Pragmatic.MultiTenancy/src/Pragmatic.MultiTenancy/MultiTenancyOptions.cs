namespace Pragmatic.MultiTenancy;

/// <summary>
///     Configuration options for multi-tenancy resolution and behavior.
/// </summary>
public sealed class MultiTenancyOptions
{
    /// <summary>
    ///     Default tenant ID for single-tenant deployments.
    ///     Default: <c>default</c>.
    /// </summary>
    public string DefaultTenantId { get; set; } = "default";

    /// <summary>
    ///     HTTP header name for header-based tenant resolution.
    ///     Default: <c>X-Tenant-Id</c>.
    /// </summary>
    public string TenantHeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    ///     Claim type for JWT claim-based tenant resolution.
    ///     Default: <c>tenant_id</c>.
    /// </summary>
    public string TenantClaimType { get; set; } = "tenant_id";

    /// <summary>
    ///     Route parameter name for route-based tenant resolution.
    ///     Default: <c>tenantId</c>.
    /// </summary>
    public string TenantRouteParameter { get; set; } = "tenantId";

    /// <summary>
    ///     Whether to require tenant resolution. If <c>true</c>, an unresolved tenant causes failure
    ///     (and tenant queries return no rows rather than the null-tenant set). Secure default.
    ///     Set to <c>false</c> only for apps that intentionally run without a resolved tenant
    ///     (and configure a system tenant for background work).
    ///     Default: <c>true</c>.
    /// </summary>
    public bool RequireTenant { get; set; } = true;

    /// <summary>
    ///     Whether to validate the resolved tenant against the authenticated user's tenant claim
    ///     (<see cref="TenantClaimType" />). Secure default.
    ///     <para>
    ///         When <c>true</c> and the request is authenticated AND the user carries a tenant claim,
    ///         the tenant produced by <i>any</i> resolution strategy (header, route, subdomain, custom)
    ///         MUST equal the claim value. On mismatch the request is rejected (fail-closed) — neither
    ///         the client-supplied value nor the claim is silently used.
    ///     </para>
    ///     <para>
    ///         This closes the cross-tenant escalation where an authenticated user from tenant A
    ///         supplies <c>X-Tenant-Id: B</c> (or <c>/b/...</c>, or <c>b.app.com</c>) and the data layer
    ///         operates as tenant B. Client-controlled strategies remain valid for pre-auth /
    ///         anonymous requests (no tenant claim present), where the resolved value is kept as-is.
    ///     </para>
    ///     Default: <c>true</c>.
    /// </summary>
    public bool EnforceTenantClaim { get; set; } = true;

    /// <summary>
    ///     Whether a resolved tenant must be in <see cref="TenantState.Active" /> to be served.
    ///     Secure default.
    ///     <para>
    ///         Checked only when an <see cref="ITenantStore" /> is registered and it knows the tenant:
    ///         the store is the authority on state, and where there is no store there is no state to
    ///         read. A tenant the store returns in any other state is rejected with HTTP 403.
    ///     </para>
    ///     <para>
    ///         Without this, <c>TenantState</c> is a value the system writes and never acts on.
    ///         Suspending a tenant, deactivating it, or catching one mid-provisioning would record the
    ///         state and change nothing — the requests would keep being served, including in the case
    ///         the migration orchestrator writes <see cref="TenantState.Suspended" /> for, where the
    ///         tenant's schema is in an indeterminate state.
    ///     </para>
    ///     <para>
    ///         Set to <c>false</c> if administration endpoints must remain reachable while addressed
    ///         as the suspended tenant itself. Reactivating from a management context — a different
    ///         tenant, or none — works either way.
    ///     </para>
    ///     Default: <c>true</c>.
    /// </summary>
    public bool EnforceTenantState { get; set; } = true;

    /// <summary>
    ///     Whether a resolved tenant must exist in the <see cref="ITenantStore" /> to be served.
    ///     Rejected with HTTP 404 — an unknown tenant is not a forbidden one, and answering
    ///     "forbidden" would confirm that the id names something.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Off by default, unlike every other guard here, and the reason is a fact about the
    ///         framework rather than a preference.</b> When multi-tenancy is detected the generated
    ///         host registers an <b>empty</b> <c>InMemoryTenantStore</c>. Defaulting this to
    ///         <c>true</c> would answer 404 to every request of every multi-tenant application until
    ///         someone populated that store — "unknown" cannot mean anything while the default list
    ///         is empty.
    ///     </para>
    ///     <para>
    ///         Turn it on where the store really is the list of tenants that exist. It is worth
    ///         turning on: header, route and subdomain read the tenant straight out of the request, so
    ///         without this an invented id becomes the request's tenant. Reads then filter on a tenant
    ///         with no rows, which is not a leak — but writes create rows under a tenant that does not
    ///         exist, and nothing says so.
    ///     </para>
    ///     <para>
    ///         Separate from <see cref="EnforceTenantState" /> because it asks a stronger question.
    ///         That one trusts the store about tenants it knows; this one trusts it about which
    ///         tenants exist at all.
    ///     </para>
    ///     Default: <c>false</c>.
    /// </remarks>
    public bool RequireKnownTenant { get; set; }
}

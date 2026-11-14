namespace Pragmatic.MultiTenancy;

/// <summary>
///     The <c>code</c> of each ProblemDetails <see cref="TenantResolutionMiddleware" /> refuses a request with: what a client branches
///     on, stable across languages, while the title and detail go through the host's <c>IErrorMessageResolver</c>.
/// </summary>
public static class TenantRefusalCodes
{
    /// <summary>400: no tenant was resolved, and the caller is authenticated or the route allows anonymous callers.</summary>
    public const string Required = "TENANT_REQUIRED";

    /// <summary>403: the resolved tenant is not the one the caller's tenant claim names.</summary>
    public const string Mismatch = "TENANT_MISMATCH";

    /// <summary>403: the tenant is suspended (<see cref="TenantState.Suspended" />).</summary>
    public const string Suspended = "TENANT_SUSPENDED";

    /// <summary>
    ///     403: the tenant is in another state that is not <see cref="TenantState.Active" /> — migrating, provisioning or deactivated; the
    ///     ProblemDetails names it in <c>tenantState</c>.
    /// </summary>
    public const string NotActive = "TENANT_NOT_ACTIVE";

    /// <summary>404: the tenant store does not know the tenant (<c>RequireKnownTenant</c>).</summary>
    public const string Unknown = "TENANT_UNKNOWN";

    /// <summary>400: a tenant was resolved on a route that runs in none (<see cref="NoTenantEndpoint" />, <c>[NoTenant]</c>).</summary>
    public const string NotAllowed = "TENANT_NOT_ALLOWED";
}

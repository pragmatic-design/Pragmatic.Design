namespace Pragmatic.Identity;

/// <summary>
///     Configuration options for identity claim mapping.
///     Controls which claim types are used to populate <see cref="ICurrentUser" /> properties.
/// </summary>
public sealed class IdentityOptions
{
    /// <summary>
    ///     Claim type used for the user's unique identifier.
    ///     Default: <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
    /// </summary>
    public string UserIdClaimType { get; set; } = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";

    /// <summary>
    ///     Claim type used for the user's display name.
    ///     Default: <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name</c>
    /// </summary>
    public string DisplayNameClaimType { get; set; } = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";

    /// <summary>
    ///     Claim type used for user roles.
    ///     Default: <c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c>
    /// </summary>
    public string RoleClaimType { get; set; } = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    /// <summary>
    ///     Claim type used for permissions.
    ///     Default: <c>permission</c>
    /// </summary>
    public string PermissionClaimType { get; set; } = "permission";

    /// <summary>
    ///     Claim type used for the tenant identifier.
    ///     Default: <c>tenant_id</c>
    /// </summary>
    public string TenantClaimType { get; set; } = "tenant_id";

    /// <summary>
    ///     Claim type used for group membership. Groups are expanded to roles (and roles to
    ///     permissions) by the authorization pipeline, so this must be normalized for the
    ///     documented "map groups to roles" path to work.
    ///     Default: <c>group</c>
    /// </summary>
    public string GroupClaimType { get; set; } = "group";

    /// <summary>
    ///     Claim type used for scopes (e.g. OAuth2 / OIDC <c>scope</c>).
    ///     Default: <c>scope</c>
    /// </summary>
    public string ScopeClaimType { get; set; } = "scope";
}

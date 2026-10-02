namespace Pragmatic.Authorization.Configuration;

/// <summary>
///     Options for the Pragmatic authorization system.
/// </summary>
public sealed class AuthorizationOptions
{
    /// <summary>
    ///     Whether to enable request-scoped permission caching.
    ///     Default: <c>true</c>.
    /// </summary>
    public bool EnablePermissionCaching { get; set; } = true;

    /// <summary>
    ///     Whether to trust <c>permission</c> claims baked into the caller's token/principal
    ///     and grant them directly. Default: <c>false</c> (server-side resolution).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Default (<c>false</c>) — server-side resolution.</b> Baked <c>permission</c> claims are
    ///         ignored: the <see cref="Providers.ClaimsPermissionProvider"/> is not registered. Permissions
    ///         are derived on the server by expanding the caller's <c>role</c>/<c>group</c> claims against the
    ///         role/group stores on every request. Roles still come from the signed token, but the authority
    ///         they grant is resolved live — so revoking a role's permissions (and calling the
    ///         <see cref="Evaluation.IPermissionCacheInvalidator"/>) takes effect immediately, and a spoofed or
    ///         remapped <c>permission</c> claim injects no authority.
    ///     </para>
    ///     <para>
    ///         <b>Opt-in (<c>true</c>) — stateless.</b> Bake <c>role</c>/<c>permission</c> claims into the token
    ///         and trust them, avoiding the per-request store round-trip. This trades away immediate revocation:
    ///         a baked permission stays valid until the token expires. Use it only with short token TTLs and by
    ///         rotating the security stamp on every authorization change, so revocation latency is bounded by the
    ///         token lifetime. Appropriate for dev/test hosts that assert identity via client headers.
    ///     </para>
    /// </remarks>
    public bool TrustPermissionClaims { get; set; }

    /// <summary>
    ///     Whether an actor in one tenant may act for a subject in another. Off.
    /// </summary>
    /// <remarks>
    ///     Nothing in the design forbade it, and nothing in the runtime defined which tenant a
    ///     delegated session belongs to — so a cross-tenant delegation would have silently picked one.
    ///     The tenant is the <b>subject's</b>, and a delegation whose two parties differ is refused
    ///     unless this is turned on, which a SaaS vendor doing cross-tenant support legitimately needs.
    ///     On a boundary of isolation the only acceptable default is to fail closed.
    /// </remarks>
    public bool AllowCrossTenantDelegation { get; set; }

    /// <summary>
    ///     How many actors may appear in one delegation chain. Two by default.
    /// </summary>
    /// <remarks>
    ///     An agent starting an agent is legitimate; an unbounded chain is a slow way back to full
    ///     authority, because every hop is another opportunity for a policy that widens.
    /// </remarks>
    public int MaxDelegationChainDepth { get; set; } = 2;

    /// <summary>
    ///     Options for cross-request permission caching. Null means no cross-request caching
    ///     (request-scoped lazy resolution only).
    /// </summary>
    public PermissionCacheOptions? CacheOptions { get; set; }

    // There is deliberately no DefaultEndpointPolicy here. One existed, defaulted to
    // RequireAuthenticated and was documented as fail-closed — and nothing ever read it: written in two
    // places, consumed in none, so setting it changed nothing at all.
    //
    // What it promised is delivered elsewhere, and there:
    //   * authenticated-by-default is PragmaticEndpointsOptions.RequireAuthorizationByDefault (true by
    //     default), applied by the generated root group; opt out per endpoint with [AllowAnonymous].
    //   * a permission for operations that declare none is auto-derivation
    //     ([PragmaticAutoDerivePermissions] / the PragmaticAutoDerivePermissions build property).
    // Two switches deciding one thing is how they come to disagree, so this module does not add a third.
}

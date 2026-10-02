using Pragmatic.Authorization;

namespace Pragmatic.Identity;

/// <summary>
///     Represents the currently authenticated user in the request scope.
///     Inject as a scoped service to access identity information anywhere in the pipeline.
/// </summary>
/// <remarks>
///     <para>
///     Authorization and authentication concerns are separated into dedicated sub-objects:
///     <see cref="Authorization"/> for permission/role checks and <see cref="Authentication"/>
///     for authentication metadata (scheme, issuer, MFA, etc.).
///     </para>
///     <para>
///     This contract is consumed by:
///     <list type="bullet">
///         <item><description>Persistence — AuditingInterceptor (CreatedBy/UpdatedBy)</description></item>
///         <item><description>Actions — AuthorizationFilter (permission checks via Authorization)</description></item>
///         <item><description>Endpoints — [RequirePermission] enforcement via Authorization</description></item>
///     </list>
///     </para>
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface ICurrentUser
{
    /// <summary>
    ///     Unique identifier for the user. Empty string for anonymous users.
    /// </summary>
    string Id { get; }

    /// <summary>
    ///     Human-readable display name. Null for anonymous users.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    ///     Whether the user has been authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    ///     Identifies the type of principal (Anonymous, User, Service, System).
    /// </summary>
    PrincipalKind Kind { get; }

    /// <summary>
    ///     Tenant identifier for multi-tenant scenarios. Null when not applicable.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    ///     Claims associated with the user, keyed by claim type.
    ///     Multi-valued: each claim type maps to a list of values.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; }

    /// <summary>
    ///     Set when this session is acting <em>on behalf of</em> someone else; <c>null</c> otherwise,
    ///     which is the overwhelming majority of sessions.
    /// </summary>
    /// <remarks>
    ///     Replaces the former <c>ImpersonatedBy</c>, which carried the impersonator's id and which
    ///     <b>nothing consulted when deciding</b>: an impersonated session was authorised exactly as
    ///     the effective principal, with no relation to whoever was impersonating. The id is now
    ///     <see cref="IDelegationContext.ActorId" />, and the authority is genuinely composed.
    /// </remarks>
    /// <remarks>
    ///     Default <c>null</c>: a session is not delegated unless something says so, and an
    ///     implementation that predates delegation must keep compiling and keep meaning what it meant.
    ///     Only the accessors that can actually carry one override it.
    /// </remarks>
    IDelegationContext? Delegation => null;

    /// <summary>
    ///     Authorization context: roles, permissions, groups, scopes, and permission checks.
    /// </summary>
    IUserAuthorization Authorization { get; }

    /// <summary>
    ///     Authentication metadata: scheme, protocol, issuer, MFA status, expiration.
    /// </summary>
    IAuthenticationContext Authentication { get; }
}

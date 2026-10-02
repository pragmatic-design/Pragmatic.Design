using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Authorization;

namespace Pragmatic.Identity;

/// <summary>
///     Resolves <see cref="ICurrentUser" /> from the current HTTP context's <see cref="ClaimsPrincipal" />.
///     Claim type mapping is configurable via <see cref="IdentityOptions" />.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IUserAuthorization"/> is resolved lazily via <see cref="IServiceProvider"/>
///         to break the circular dependency: ICurrentUser → IUserAuthorization(CachedPermissionResolver) → ICurrentUser.
///     </para>
///     <para>
///         The <c>_authorization</c> and <c>_claims</c> memo fields use plain null-coalescing assignment,
///         not a thread-safe cache. This is intentional: the accessor is registered with a scoped (per-HTTP-request)
///         lifetime and a request is served on a single logical thread, so concurrent access does not occur.
///         Do NOT register this type as a singleton.
///     </para>
/// </remarks>
public sealed class ClaimsPrincipalUserAccessor(
    IHttpContextAccessor httpContextAccessor,
    IOptions<IdentityOptions> options,
    IServiceProvider serviceProvider) : ICurrentUser
{
    private IUserAuthorization? _authorization;
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _claims;
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;
    private IdentityOptions Options => options.Value;

    /// <inheritdoc />
    /// <remarks>
    ///     Returns <see cref="string.Empty"/> when there is no authenticated principal. Use
    ///     <see cref="IsAuthenticated"/> (or <see cref="Kind"/>) to distinguish an anonymous request from
    ///     an authenticated user — never treat an empty <see cref="Id"/> as a real identifier.
    /// </remarks>
    public string Id =>
        Principal?.FindFirst(Options.UserIdClaimType)?.Value ?? string.Empty;

    /// <inheritdoc />
    public string? DisplayName =>
        Principal?.FindFirst(Options.DisplayNameClaimType)?.Value;

    /// <inheritdoc />
    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public PrincipalKind Kind => IsAuthenticated ? PrincipalKind.User : PrincipalKind.Anonymous;

    /// <inheritdoc />
    public string? TenantId =>
        Principal?.FindFirst(Options.TenantClaimType)?.Value;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
        _claims ??= Principal?.Claims
            .GroupBy(c => NormalizeClaimType(c.Type))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(c => c.Value).ToList())
        ?? new Dictionary<string, IReadOnlyList<string>>();

    // Maps well-known long-form claim type URIs to their short names so that
    // consumers (GroupExpansionProvider, CachedPermissionResolver) can use short
    // keys like "role", "permission", "group", "scope" regardless of the underlying claim format.
    private string NormalizeClaimType(string claimType) => claimType switch
    {
        _ when claimType == Options.RoleClaimType => "role",
        _ when claimType == Options.PermissionClaimType => "permission",
        _ when claimType == Options.GroupClaimType => "group",
        _ when claimType == Options.ScopeClaimType => "scope",
        _ when claimType == Options.TenantClaimType => "tenant_id",
        _ when claimType == Options.UserIdClaimType => "sub",
        _ when claimType == Options.DisplayNameClaimType => "name",
        _ => claimType
    };

    /// <inheritdoc />
    /// <remarks>
    ///     Built from the RFC 8693 claims the token carries: <c>act_sub</c> is the acting party, the
    ///     rest qualify it. The claim was already read here before delegation meant anything — it just
    ///     surfaced an id nobody acted on.
    /// </remarks>
    public IDelegationContext? Delegation
    {
        get
        {
            var actor = Principal?.FindFirst(DelegationClaims.ActorSubject)?.Value;
            if (string.IsNullOrEmpty(actor))
                return null;

            return new ClaimsDelegationContext(Id, actor!, Principal!);
        }
    }

    /// <inheritdoc />
    public IUserAuthorization Authorization =>
        _authorization ??= serviceProvider.GetRequiredService<IUserAuthorization>();

    /// <inheritdoc />
    public IAuthenticationContext Authentication => new ClaimsAuthenticationContext(Principal, Options);
}

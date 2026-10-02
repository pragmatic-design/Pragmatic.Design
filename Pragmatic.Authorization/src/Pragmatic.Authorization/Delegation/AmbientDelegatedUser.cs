using System.Collections.Generic;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Delegation;

/// <summary>
///     The <see cref="ICurrentUser" /> that makes <see cref="DelegationScope" /> real: while a scope is
///     open, the session <em>is</em> the subject, and the delegation is visible to everything that
///     asks.
/// </summary>
/// <remarks>
///     <para>
///         A decorator, so the inner accessor keeps answering whenever no scope is open — which in an
///         HTTP request is always, because there the delegation arrives on the token instead.
///     </para>
///     <para>
///         A request-borne delegation wins over an ambient one. Opening a scope inside a request that
///         is already delegated would otherwise silently reinterpret whose authority is in play, and
///         the honest reading of "the caller already said who they act for" is that nothing local
///         overrides it.
///     </para>
/// </remarks>
public sealed class AmbientDelegatedUser(ICurrentUser inner) : ICurrentUser
{
    private IDelegationContext? Ambient => inner.Delegation is null ? DelegationScope.Value : null;

    /// <inheritdoc />
    public string Id => Ambient?.SubjectId ?? inner.Id;

    /// <inheritdoc />
    public IDelegationContext? Delegation => inner.Delegation ?? DelegationScope.Value;

    /// <inheritdoc />
    /// <remarks>
    ///     Authenticated while a scope is open: the work has an identity, which is the whole point of
    ///     opening one instead of running as nobody — or as everybody.
    /// </remarks>
    public bool IsAuthenticated => Ambient is not null || inner.IsAuthenticated;

    /// <inheritdoc />
    public string? DisplayName => inner.DisplayName;

    /// <inheritdoc />
    public PrincipalKind Kind => inner.Kind;

    /// <inheritdoc />
    public string? TenantId => inner.TenantId;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => inner.Claims;

    /// <inheritdoc />
    public IUserAuthorization Authorization => inner.Authorization;

    /// <inheritdoc />
    public IAuthenticationContext Authentication => inner.Authentication;
}

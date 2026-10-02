using Pragmatic.Authorization;

namespace Pragmatic.Identity;

/// <summary>
///     Default <see cref="ICurrentUser" /> for unauthenticated requests.
///     All permission and role checks return <c>false</c>.
/// </summary>
/// <remarks>
///     <para>
///         Nothing needs to register this. Every consumer in the framework falls back to it at the
///         point of use:
///         <code>var user = provider.GetService&lt;ICurrentUser&gt;() ?? AnonymousUser.Instance;</code>
///     </para>
///     <para>
///         That is deliberate, and better than a registration somebody has to remember: with no
///         <see cref="ICurrentUser"/> registered, an authorization filter does not skip its checks
///         — it gets a user that fails every one of them. Failing open by omission is not
///         reachable.
///     </para>
/// </remarks>
public sealed class AnonymousUser : ICurrentUser
{
    /// <summary>
    ///     Singleton instance. Use this instead of creating new instances.
    /// </summary>
    public static readonly AnonymousUser Instance = new();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyClaims =
        new Dictionary<string, IReadOnlyList<string>>();

    private AnonymousUser() { }

    /// <inheritdoc />
    public string Id => string.Empty;

    /// <inheritdoc />
    public string? DisplayName => null;

    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public PrincipalKind Kind => PrincipalKind.Anonymous;

    /// <inheritdoc />
    public string? TenantId => null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => EmptyClaims;

    /// <inheritdoc />

    /// <inheritdoc />
    /// <inheritdoc />
    /// <remarks>
    ///     Declared rather than left to the interface default: this type is used directly, and a
    ///     default interface member is reachable only through the interface — <c>Instance.Delegation</c>
    ///     would not compile.
    /// </remarks>
    public IDelegationContext? Delegation => null;

    public IUserAuthorization Authorization => NullUserAuthorization.Instance;

    /// <inheritdoc />
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}

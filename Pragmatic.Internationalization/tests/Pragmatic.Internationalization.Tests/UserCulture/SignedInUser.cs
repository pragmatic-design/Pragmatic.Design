using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Internationalization.Tests.UserCulture;

/// <summary>
///     A signed-in <see cref="ICurrentUser" /> carrying one "sub" claim — everything the generated
///     resolver looks at, and nothing else.
/// </summary>
public sealed class SignedInUser(string subject) : ICurrentUser
{
    /// <inheritdoc />
    public string Id => subject;

    /// <inheritdoc />
    public string? DisplayName => subject;

    /// <inheritdoc />
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    public PrincipalKind Kind => PrincipalKind.User;

    /// <inheritdoc />
    public string? TenantId => null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["sub"] = [subject]
        };


    /// <inheritdoc />
    public IUserAuthorization Authorization => NullUserAuthorization.Instance;

    /// <inheritdoc />
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}

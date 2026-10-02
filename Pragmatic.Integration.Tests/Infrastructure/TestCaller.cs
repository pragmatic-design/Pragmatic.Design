using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Integration.Tests.Infrastructure;

/// <summary>An authenticated caller holding exactly the permissions it is given.</summary>
public sealed class TestCaller(string id, params string[] permissions) : ICurrentUser
{
    /// <inheritdoc />
    public string Id => id;

    /// <inheritdoc />
    public string? DisplayName => id;

    /// <inheritdoc />
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    public PrincipalKind Kind => PrincipalKind.User;

    /// <inheritdoc />
    public string? TenantId => null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <inheritdoc />
    public IUserAuthorization Authorization { get; } = new TestCallerAuthorization([.. permissions]);

    /// <inheritdoc />
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}

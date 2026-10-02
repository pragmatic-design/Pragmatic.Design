using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A controllable <see cref="ICurrentUser" /> for testing audit field population.
///     Allows setting identity properties deterministically.
/// </summary>
internal sealed class FakeCurrentUser : ICurrentUser
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyClaims =
        new Dictionary<string, IReadOnlyList<string>>();

    public string Id { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public bool IsAuthenticated { get; set; }

    public PrincipalKind Kind => IsAuthenticated ? PrincipalKind.User : PrincipalKind.Anonymous;

    public string? TenantId => null;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => EmptyClaims;


    public IUserAuthorization Authorization => NullUserAuthorization.Instance;

    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;

    /// <summary>
    ///     Creates an authenticated <see cref="FakeCurrentUser" /> with the given ID.
    /// </summary>
    public static FakeCurrentUser Authenticated(string id, string? displayName = null)
        => new() { Id = id, IsAuthenticated = true, DisplayName = displayName };

    /// <summary>
    ///     Creates an anonymous (unauthenticated) <see cref="FakeCurrentUser" />.
    /// </summary>
    public static FakeCurrentUser Anonymous()
        => new() { Id = string.Empty, IsAuthenticated = false };
}

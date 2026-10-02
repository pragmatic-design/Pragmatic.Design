using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Persistence.Tests.Fakes;

/// <summary>
///     An <see cref="ICurrentUser" /> that is authenticated or not, with a fixed permission set.
/// </summary>
/// <remarks>
///     ⚠️ Whether this is authenticated changes far more than it looks. With no authenticated user and
///     a permission-based filter in play, <c>DefaultQueryFilterProvider</c> fails <b>closed</b> — the
///     combined predicate becomes <c>_ =&gt; false</c> rather than losing the filter — so a test that
///     forgets to supply one measures the fail-closed path and reads it as the filter working.
/// </remarks>
internal sealed class FakeCurrentUser(bool isAuthenticated, string[]? permissions = null) : ICurrentUser
{
    public string Id => "test-user";

    public string? DisplayName => "Test";

    public bool IsAuthenticated => isAuthenticated;

    public PrincipalKind Kind => isAuthenticated ? PrincipalKind.User : PrincipalKind.Anonymous;

    public string? TenantId => null;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
        new Dictionary<string, IReadOnlyList<string>>();

    public IUserAuthorization Authorization => new FakeAuthorization(permissions);

    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}

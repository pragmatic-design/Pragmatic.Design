using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Persistence.Benchmarks.Workload;

/// <summary>
///     A signed-in caller. Ownership and scopes enumerate the tracker only when there is one, so an
///     anonymous caller would measure a shorter chain than a request does.
/// </summary>
public sealed class SignedInUser : ICurrentUser
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoClaims =
        new Dictionary<string, IReadOnlyList<string>>();

    public string Id => "bench-user";
    public string? DisplayName => "Bench";
    public bool IsAuthenticated => true;
    public PrincipalKind Kind => PrincipalKind.User;
    public string? TenantId => null;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => NoClaims;
    public IUserAuthorization Authorization => NullUserAuthorization.Instance;
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}

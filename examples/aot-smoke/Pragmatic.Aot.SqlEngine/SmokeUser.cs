using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>The caller: authenticated, no permission.</summary>
internal sealed class SmokeUser(string id) : ICurrentUser
{
    public string Id => id;
    public string? DisplayName => id;
    public bool IsAuthenticated => true;
    public PrincipalKind Kind => PrincipalKind.User;
    public string? TenantId => null;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } = new Dictionary<string, IReadOnlyList<string>>();
    public IUserAuthorization Authorization => NoPermissions.Instance;
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}

using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Configuration.Management.Tests.Unit;

/// <summary>
///     The caller a management action runs for: an operator with no tenant, or an administrator of one.
/// </summary>
internal sealed class TestCaller(string? tenantId) : ICurrentUser
{
    /// <summary>A caller with no tenant: manages the base values and every tenant.</summary>
    public static readonly ICurrentUser Operator = new TestCaller(tenantId: null);

    /// <summary>An administrator of <paramref name="tenant" />.</summary>
    public static ICurrentUser Of(string tenant) => new TestCaller(tenant);

    public string Id => "admin";
    public string? DisplayName => null;
    public bool IsAuthenticated => true;
    public PrincipalKind Kind => PrincipalKind.User;
    public string? TenantId => tenantId;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
        => new Dictionary<string, IReadOnlyList<string>>();
    public IUserAuthorization Authorization => NullUserAuthorization.Instance;
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}

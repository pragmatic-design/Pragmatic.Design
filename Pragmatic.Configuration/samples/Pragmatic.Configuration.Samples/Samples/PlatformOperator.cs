using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     The caller the management sample runs as: an operator who belongs to no tenant, and so may manage
///     the base values. An administrator of one tenant would be refused them.
/// </summary>
internal sealed class PlatformOperator : ICurrentUser
{
    public static readonly PlatformOperator Instance = new();

    public string Id => "operator";
    public string? DisplayName => "Platform operator";
    public bool IsAuthenticated => true;
    public PrincipalKind Kind => PrincipalKind.User;
    public string? TenantId => null;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
        => new Dictionary<string, IReadOnlyList<string>>();
    public IUserAuthorization Authorization => NullUserAuthorization.Instance;
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}

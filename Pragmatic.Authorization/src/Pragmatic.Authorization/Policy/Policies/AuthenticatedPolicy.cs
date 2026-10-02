using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to be authenticated. Singleton.
/// </summary>
internal sealed class AuthenticatedPolicy : ResourcePolicy
{
    public static readonly AuthenticatedPolicy Instance = new();

    private AuthenticatedPolicy() { }

    public override bool Evaluate(ICurrentUser user) => user.IsAuthenticated;
}

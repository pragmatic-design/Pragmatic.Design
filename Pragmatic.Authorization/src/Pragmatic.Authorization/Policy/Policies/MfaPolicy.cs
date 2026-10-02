using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to have completed multi-factor authentication. Singleton.
/// </summary>
/// <remarks>
///     <c>IAuthenticationContext.IsMfaAuthenticated</c> is populated from the claims; without a
///     decision point that reads it, MFA could be observed and never required. This is the point
///     that reads it, and it is a policy rather than a mechanism of its own: MFA is one condition
///     among the ones a resource can demand, so it composes with the others through
///     <c>[RequirePolicy&lt;T&gt;]</c> and the And/Or/Not combinators instead of arriving with its
///     own attribute and its own filter.
/// </remarks>
internal sealed class MfaPolicy : ResourcePolicy
{
    public static readonly MfaPolicy Instance = new();

    private MfaPolicy() { }

    public override bool Evaluate(ICurrentUser user) => user.Authentication.IsMfaAuthenticated;
}

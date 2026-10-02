using Pragmatic.Composition.Attributes;
using Pragmatic.Identity.Local.Actions;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Turns what a sign-in established about the caller into the token they send on every request.
/// </summary>
/// <remarks>
///     The token format is the host's choice: <c>UseJwtAuthentication</c> registers the JWT one. A module
///     depends on this contract, never on the generator behind it — which exists only once the host has
///     made that call, and which a module's generator could not see. Until the host chooses,
///     the package's <see cref="UnconfiguredAccessTokenIssuer" /> refuses to sign.
/// </remarks>
[ProvidedByHost]
public interface IAccessTokenIssuer
{
    /// <summary>Signs a token that carries <paramref name="claims" />, the security stamp included.</summary>
    AccessToken Issue(SignInClaims claims);
}

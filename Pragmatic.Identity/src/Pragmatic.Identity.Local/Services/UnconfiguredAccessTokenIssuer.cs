using Pragmatic.Composition.Attributes;
using Pragmatic.Identity.Local.Actions;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     The issuer a host has until it chooses one: it signs nothing, and says what to call.
/// </summary>
/// <remarks>
///     Every action of the package is registered in a host that imports it, the sign-in included, and the
///     sign-in's invoker takes an issuer. Without this default, a host that signs no token — its tokens
///     come from another provider, or it has none in development — fails the container's validation at
///     startup for an action it never runs. With it, that host starts, and a sign-in it did not configure
///     fails on the first call, naming the fix. The host's own registration replaces it, whichever comes
///     first: this one is added only if none is there.
/// </remarks>
[Service(Lifetime = Lifetime.Singleton)]
public sealed class UnconfiguredAccessTokenIssuer : IAccessTokenIssuer
{
    /// <inheritdoc />
    public AccessToken Issue(SignInClaims claims) =>
        throw new InvalidOperationException(
            "No access token issuer is configured: the credentials were checked, but nothing can sign the token. " +
            "Call UseJwtAuthentication() in the host, or register an IAccessTokenIssuer.");
}

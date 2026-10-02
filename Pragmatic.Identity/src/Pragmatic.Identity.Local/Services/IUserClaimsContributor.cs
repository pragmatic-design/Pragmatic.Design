namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     What an application adds to the token of a sign-in: who the caller is to it, their name, their
///     roles, their tenant.
/// </summary>
/// <remarks>
///     The credentials are the package's to check and the token the host's to sign; what the account
///     means to the application is the application's, and this is where it says it. Every contributor
///     registered runs, in registration order, after the credentials are accepted and before the token is
///     signed.
/// </remarks>
public interface IUserClaimsContributor
{
    /// <summary>Completes <paramref name="claims" /> for a sign-in to <paramref name="identity" />.</summary>
    ValueTask ContributeAsync(LocalIdentity identity, SignInClaims claims, CancellationToken ct = default);
}

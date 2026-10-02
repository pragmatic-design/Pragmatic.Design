using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Pragmatic.Identity.Local;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     The producer half of the external identity key round trip: a token minted from a local login
///     carries the stored key outright.
/// </summary>
/// <remarks>
///     Registration writes <c>local|{email}</c>. If the generator put that whole value in <c>sub</c>, the
///     reader would compose <c>{token issuer}|{sub}</c> — a third value matching no stored identity, so
///     everything built on the resolver would be inert, and silently so.
///     The reader half is asserted in <c>Pragmatic.Identity.AspNetCore.Tests</c>, where the context
///     that consumes this claim is visible.
/// </remarks>
public class ExternalIdentityKeyClaimTests
{
    private const string TokenIssuer = "https://app.example.com";

    private static JwtTokenGenerator Generator() => new(Options.Create(new JwtOptions
    {
        SigningKey = "round-trip-signing-key-at-least-32-chars!",
        Issuer = TokenIssuer,
    }));

    private static string? ClaimOf(string token, string type)
        => new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .FirstOrDefault(c => c.Type == type)?.Value;

    [Fact]
    public void ATokenFromALoginResult_CarriesTheStoredKeyAsAClaim()
    {
        var stored = ExternalIdentityKey.Compose(LocalIdentity.Provider, "alice@example.com")!;

        var token = Generator().Generate(new LoginResult(stored, DateTimeOffset.UnixEpoch));

        ClaimOf(token.Token, ExternalIdentityKey.ClaimType).Should().Be(stored);
    }

    /// <summary>
    ///     The claim is what makes the key survive; the subject alone does not, because the reader
    ///     would wrap it in the token's issuer.
    /// </summary>
    [Fact]
    public void TheClaimIsNotTheIssuerWrappedAroundTheSubject()
    {
        var stored = ExternalIdentityKey.Compose(LocalIdentity.Provider, "alice@example.com")!;

        var token = Generator().Generate(new LoginResult(stored, DateTimeOffset.UnixEpoch));

        ClaimOf(token.Token, ExternalIdentityKey.ClaimType)
            .Should().NotBe(ExternalIdentityKey.Compose(TokenIssuer, stored));
    }

    /// <summary>
    ///     A plain subject carries no such claim, so a pure OIDC token still composes on the reader
    ///     side — and there the token issuer really is the identity provider.
    /// </summary>
    [Fact]
    public void ATokenFromAPlainSubject_CarriesNoSuchClaim()
    {
        var token = Generator().Generate(subject: "oidc-subject-1");

        ClaimOf(token.Token, ExternalIdentityKey.ClaimType).Should().BeNull();
    }
}

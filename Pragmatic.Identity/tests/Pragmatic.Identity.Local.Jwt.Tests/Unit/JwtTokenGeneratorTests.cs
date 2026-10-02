using System.IdentityModel.Tokens.Jwt;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Identity.Local.Jwt;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

public sealed class JwtTokenGeneratorTests
{
    // 32+ byte signing key satisfies the HMAC-SHA256 minimum-strength requirement.
    private const string ValidKey = "this-is-a-sufficiently-long-signing-key-1234567890";

    private readonly JwtOptions _optionsValue = new()
    {
        SigningKey = ValidKey,
        Issuer = "pragmatic-issuer",
        Audience = "pragmatic-audience",
        TokenExpiration = TimeSpan.FromMinutes(30)
    };

    [Fact]
    public void Generate_WithSubject_ReturnsNonEmptyToken()
    {
        var result = CreateGenerator().Generate("local|user@example.com");

        result.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Generate_WithSubject_SetsExpiryFromTokenExpiration()
    {
        var before = DateTimeOffset.UtcNow;

        var result = CreateGenerator().Generate("local|user@example.com");

        result.ExpiresAt.Should().BeCloseTo(
            before.Add(_optionsValue.TokenExpiration), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Generate_WithSubject_ProducesParseableJwt()
    {
        var result = CreateGenerator().Generate("local|user@example.com");

        var act = () => new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        act.Should().NotThrow();
    }

    [Fact]
    public void Generate_WithSubject_EmbedsIssuerAudienceAndSubject()
    {
        var result = CreateGenerator().Generate("local|user@example.com");

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        token.Issuer.Should().Be(_optionsValue.Issuer);
        token.Audiences.Should().Contain(_optionsValue.Audience!);
        token.Subject.Should().Be("local|user@example.com");
    }

    [Fact]
    public void Generate_WithDisplayNameAndTenant_EmbedsOptionalClaims()
    {
        var result = CreateGenerator().Generate(
            "local|user@example.com", displayName: "Jane Doe", tenantId: "tenant-A");

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        token.Claims.Should().Contain(c => c.Type == "name" && c.Value == "Jane Doe");
        token.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == "tenant-A");
    }

    [Fact]
    public void Generate_WithoutOptionalClaims_OmitsThem()
    {
        var result = CreateGenerator().Generate("local|user@example.com");

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        token.Claims.Should().NotContain(c => c.Type == "name");
        token.Claims.Should().NotContain(c => c.Type == "tenant_id");
        token.Claims.Should().NotContain(c => c.Type == "sstamp");
    }

    [Fact]
    public void Generate_WithRolesAndPermissions_EmbedsEachAsSeparateClaim()
    {
        var result = CreateGenerator().Generate(
            "local|user@example.com",
            roles: ["admin", "editor"],
            permissions: ["orders.read", "orders.write"]);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        token.Claims.Where(c => c.Type == "role").Select(c => c.Value)
            .Should().BeEquivalentTo("admin", "editor");
        token.Claims.Where(c => c.Type == "permission").Select(c => c.Value)
            .Should().BeEquivalentTo("orders.read", "orders.write");
    }

    [Fact]
    public void Generate_WithSecurityStamp_EmbedsSstampClaim()
    {
        var result = CreateGenerator().Generate(
            "local|user@example.com", securityStamp: "stamp-123");

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        token.Claims.Should().Contain(c => c.Type == "sstamp" && c.Value == "stamp-123");
    }

    [Fact]
    public void Generate_TwoTokens_HaveDistinctJti()
    {
        var generator = CreateGenerator();
        var handler = new JwtSecurityTokenHandler();

        var first = handler.ReadJwtToken(generator.Generate("a").Token);
        var second = handler.ReadJwtToken(generator.Generate("b").Token);

        var firstJti = first.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var secondJti = second.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        firstJti.Should().NotBe(secondJti);
    }

    [Fact]
    public void Generate_WithShortSigningKey_Throws()
    {
        var weak = new JwtOptions { SigningKey = "too-short" };
        var generator = new JwtTokenGenerator(Options.Create(weak));

        var act = () => generator.Generate("local|user@example.com");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 32 bytes*");
    }

    private JwtTokenGenerator CreateGenerator() => new(Options.Create(_optionsValue));
}

using System.Security.Claims;
using Pragmatic.Testing.Assertions;
using Pragmatic.Identity;

namespace Pragmatic.Identity.AspNetCore.Tests.Unit;

public sealed class ClaimsAuthenticationContextTests
{
    private readonly IdentityOptions _options = new();

    [Fact]
    public void IsMfaAuthenticated_WithExactMfaAmr_ReturnsTrue()
    {
        var context = Create(new Claim("amr", "mfa"));

        context.IsMfaAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void IsMfaAuthenticated_WithMixedCaseMfaAmr_ReturnsTrue()
    {
        var context = Create(new Claim("amr", "MFA"));

        context.IsMfaAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void IsMfaAuthenticated_WithSubstringAmr_ReturnsFalse()
    {
        // "smfa" contains "mfa" — a substring test would wrongly elevate. Equality must reject it.
        var context = Create(new Claim("amr", "smfa"));

        context.IsMfaAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void IsMfaAuthenticated_WithMultipleAmrIncludingMfa_ReturnsTrue()
    {
        var context = Create(new Claim("amr", "pwd"), new Claim("amr", "mfa"));

        context.IsMfaAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void IsMfaAuthenticated_WithNoAmr_ReturnsFalse()
    {
        var context = Create(new Claim("sub", "user-1"));

        context.IsMfaAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void IsMfaAuthenticated_NullPrincipal_ReturnsFalse()
    {
        var context = new ClaimsAuthenticationContext(principal: null, _options);

        context.IsMfaAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void ExternalIdentityKey_PercentEscapesComponents_BeforeJoining()
    {
        var context = Create(
            new Claim("iss", "https://issuer/"),
            new Claim(_options.UserIdClaimType, "sub|value"));

        // Both components are percent-escaped; the '|' inside the subject becomes %7C.
        context.ExternalIdentityKey.Should()
            .Be($"{Uri.EscapeDataString("https://issuer/")}|{Uri.EscapeDataString("sub|value")}");
        context.ExternalIdentityKey.Should().Be("https%3A%2F%2Fissuer%2F|sub%7Cvalue");
    }

    [Fact]
    public void ExternalIdentityKey_SeparatorPlacement_NoCollisionAcrossDifferentSplits()
    {
        // issuer "a|b" + subject "c" must NOT produce the same key as issuer "a" + subject "b|c".
        var left = Create(new Claim("iss", "a|b"), new Claim(_options.UserIdClaimType, "c"));
        var right = Create(new Claim("iss", "a"), new Claim(_options.UserIdClaimType, "b|c"));

        left.ExternalIdentityKey.Should().NotBe(right.ExternalIdentityKey);
    }

    [Fact]
    public void ExternalIdentityKey_MissingIssuer_ReturnsNull()
    {
        var context = Create(new Claim(_options.UserIdClaimType, "sub-1"));

        context.ExternalIdentityKey.Should().BeNull();
    }

    [Fact]
    public void ExternalIdentityKey_MissingSubject_ReturnsNull()
    {
        var context = Create(new Claim("iss", "https://issuer/"));

        context.ExternalIdentityKey.Should().BeNull();
    }

    [Fact]
    public void ExternalIdentityKey_NullPrincipal_ReturnsNull()
    {
        var context = new ClaimsAuthenticationContext(principal: null, _options);

        context.ExternalIdentityKey.Should().BeNull();
    }

    [Fact]
    public void Members_NullPrincipal_AreNullSafe()
    {
        var context = new ClaimsAuthenticationContext(principal: null, _options);

        context.Scheme.Should().BeNull();
        context.Protocol.Should().BeNull();
        context.Issuer.Should().BeNull();
        context.Subject.Should().BeNull();
        context.AuthenticatedAt.Should().BeNull();
        context.ExpiresAt.Should().BeNull();
    }

    // =========================================================================
    // ExternalIdentityKey — the claim first, composing second
    // =========================================================================

    /// <summary>
    ///     A token whose issuer is not the identity provider says the key outright, and that value is
    ///     what the reader must return. Composing from <c>iss</c> would name whoever signed the token,
    ///     and the resolver looking a user up by this key would match nobody.
    /// </summary>
    [Fact]
    public void ExternalIdentityKey_WithTheClaimPresent_ReturnsItVerbatim()
    {
        var context = Create(
            new Claim(ExternalIdentityKey.ClaimType, "local|alice%40example.com"),
            new Claim("iss", "https://app.example.com"),
            new Claim(new IdentityOptions().UserIdClaimType, "local|alice@example.com"));

        context.ExternalIdentityKey.Should().Be("local|alice%40example.com");
    }

    /// <summary>
    ///     Without the claim — a plain OIDC token — the key is composed, because there the token
    ///     issuer really is the identity provider.
    /// </summary>
    [Fact]
    public void ExternalIdentityKey_WithoutTheClaim_ComposesFromIssuerAndSubject()
    {
        var context = Create(
            new Claim("iss", "https://idp.example.com"),
            new Claim(new IdentityOptions().UserIdClaimType, "subject-1"));

        context.ExternalIdentityKey
            .Should().Be(ExternalIdentityKey.Compose("https://idp.example.com", "subject-1"));
    }

    /// <summary>Neither route can produce a key from half the input.</summary>
    [Fact]
    public void ExternalIdentityKey_WithNoIssuerAndNoClaim_IsNull()
    {
        var context = Create(new Claim(new IdentityOptions().UserIdClaimType, "subject-1"));

        context.ExternalIdentityKey.Should().BeNull();
    }

    private ClaimsAuthenticationContext Create(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsAuthenticationContext(new ClaimsPrincipal(identity), _options);
    }
}

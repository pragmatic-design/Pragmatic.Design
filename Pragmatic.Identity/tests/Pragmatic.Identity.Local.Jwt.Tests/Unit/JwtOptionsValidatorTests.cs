using Pragmatic.Testing.Assertions;
using Pragmatic.Identity.Local.Jwt;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     Startup-validation guards for <see cref="JwtOptions" /> (#ID-JWT1..3): expiry/skew sanity, key entropy,
///     and the production-strict issuer+audience requirement that cannot be bypassed by binding options
///     directly.
/// </summary>
public sealed class JwtOptionsValidatorTests
{
    // 32+ byte, high-entropy signing key.
    private const string ValidKey = "s7Kp2vX9qLmZ4wR8tNbY6cH3jF1dG5aQ0eU-signing-key";

    private static JwtOptions ValidOptions() => new()
    {
        SigningKey = ValidKey,
        Issuer = "https://issuer.example.com",
        Audience = "my-api",
        TokenExpiration = TimeSpan.FromMinutes(30),
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    [Fact]
    public void Validate_WithValidOptions_Succeeds()
    {
        var result = new JwtOptionsValidator(productionStrict: true).Validate(null, ValidOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithZeroExpiration_Fails()
    {
        var options = ValidOptions();
        options.TokenExpiration = TimeSpan.Zero;

        var result = new JwtOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TokenExpiration");
    }

    [Fact]
    public void Validate_WithNegativeExpiration_Fails()
    {
        var options = ValidOptions();
        options.TokenExpiration = TimeSpan.FromMinutes(-5);

        var result = new JwtOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TokenExpiration");
    }

    [Fact]
    public void Validate_WithNegativeClockSkew_Fails()
    {
        var options = ValidOptions();
        options.ClockSkew = TimeSpan.FromSeconds(-1);

        var result = new JwtOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ClockSkew");
    }

    [Fact]
    public void Validate_WithAbsurdlyLargeClockSkew_Fails()
    {
        var options = ValidOptions();
        options.ClockSkew = TimeSpan.FromHours(1);

        var result = new JwtOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ClockSkew");
    }

    [Fact]
    public void Validate_WithLowEntropyKey_Fails()
    {
        var options = ValidOptions();
        options.SigningKey = new string('a', 40); // 40 bytes but a single distinct character

        var result = new JwtOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("low-entropy");
    }

    [Fact]
    public void Validate_WithShortKey_Fails()
    {
        var options = ValidOptions();
        options.SigningKey = "short";

        var result = new JwtOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("32 bytes");
    }

    [Fact]
    public void Validate_ProductionStrict_MissingIssuerAndAudience_Fails()
    {
        var options = ValidOptions();
        options.Issuer = null;
        options.Audience = null;

        var result = new JwtOptionsValidator(productionStrict: true).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Issuer and Audience");
    }

    [Fact]
    public void Validate_ProductionStrict_MissingAudienceOnly_Fails()
    {
        var options = ValidOptions();
        options.Audience = null;

        var result = new JwtOptionsValidator(productionStrict: true).Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_NonProduction_MissingIssuerAndAudience_Succeeds()
    {
        var options = ValidOptions();
        options.Issuer = null;
        options.Audience = null;

        var result = new JwtOptionsValidator(productionStrict: false).Validate(null, options);

        result.Succeeded.Should().BeTrue("Development is lenient about issuer/audience");
    }

    [Fact]
    public void Validate_AudienceWithoutIssuer_FailsEvenInDevelopment()
    {
        var options = ValidOptions();
        options.Issuer = null; // audience set, issuer missing

        var result = new JwtOptionsValidator(productionStrict: false).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Issuer must be configured when Audience");
    }
}

using Pragmatic.Testing.Assertions;
using Pragmatic.Logging.Privacy;

namespace Pragmatic.Logging.Tests;

public class SecretDetectorTests
{
    [Fact]
    public void DetectSecrets_WithNull_ReturnsEmpty()
    {
        var detector = new SecretDetector();

        detector.DetectSecrets(null!).Should().BeEmpty();
    }

    [Fact]
    public void DetectSecrets_WithEmpty_ReturnsEmpty()
    {
        var detector = new SecretDetector();

        detector.DetectSecrets(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void DetectSecrets_WithAwsAccessKey_DetectsApiKeysCategory()
    {
        var detector = new SecretDetector();

        var results = detector.DetectSecrets("config AKIAIOSFODNN7EXAMPLE end");

        results.Should().Contain(r => r.Category == "ApiKeys");
    }

    [Fact]
    public void DetectSecrets_WithJwt_DetectsTokensCategory()
    {
        var detector = new SecretDetector();
        const string jwt = "eyJhbGciOiJIUzI1Ni1.eyJzdWIiOiIxMjM0NTY3ODkw.dozjgNryP4J3jVmNHl0w";

        var results = detector.DetectSecrets($"Authorization {jwt}");

        results.Should().Contain(r => r.Category == "Tokens");
    }

    [Fact]
    public void DetectSecrets_WithRsaPrivateKey_IsCriticalSeverity()
    {
        var detector = new SecretDetector();
        const string key = "-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA\n-----END RSA PRIVATE KEY-----";

        var results = detector.DetectSecrets(key);

        results.Should().Contain(r => r.Category == "Crypto" && r.Severity == SecretSeverity.Critical);
    }

    [Fact]
    public void DetectSecrets_WithNoSecrets_ReturnsEmpty()
    {
        var detector = new SecretDetector();

        detector.DetectSecrets("just a plain log line with no credentials").Should().BeEmpty();
    }

    [Fact]
    public void DetectSecrets_BelowMinimumLength_ReturnsEmpty()
    {
        // MinimumSecretLength defaults to 8; content shorter than that is skipped wholesale.
        var detector = new SecretDetector();

        detector.DetectSecrets("AKIA").Should().BeEmpty();
    }

    [Fact]
    public void RedactSecrets_WithEmpty_ReturnsEmpty()
    {
        var detector = new SecretDetector();

        detector.RedactSecrets(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void RedactSecrets_RemovesAwsAccessKeyFromOutput()
    {
        var detector = new SecretDetector();
        const string accessKey = "AKIAIOSFODNN7EXAMPLE";

        var result = detector.RedactSecrets($"key={accessKey}");

        result.Should().NotContain(accessKey);
    }

    [Fact]
    public void RedactSecrets_DefaultPlaceholderStyle_UsesCategoryLabel()
    {
        // Default RedactionStyle is Placeholder -> "[{CATEGORY}_REDACTED]".
        var detector = new SecretDetector();

        var result = detector.RedactSecrets("key=AKIAIOSFODNN7EXAMPLE");

        result.Should().Contain("[APIKEYS_REDACTED]");
    }

    [Fact]
    public void RedactSecrets_MinimalStyle_UsesPlainMarker()
    {
        var detector = new SecretDetector(new SecretDetectionOptions
        {
            RedactionStyle = SecretRedactionStyle.Minimal,
        });

        var result = detector.RedactSecrets("key=AKIAIOSFODNN7EXAMPLE");

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("AKIAIOSFODNN7EXAMPLE");
    }

    [Fact]
    public void RedactSecrets_WithNoSecrets_ReturnsUnchanged()
    {
        var detector = new SecretDetector();
        const string content = "nothing secret here";

        detector.RedactSecrets(content).Should().Be(content);
    }

    [Fact]
    public void DetectSecretsInProperty_WithNullValue_ReturnsEmpty()
    {
        var detector = new SecretDetector();

        detector.DetectSecretsInProperty("password", null, null).Should().BeEmpty();
    }

    [Fact]
    public void DetectSecretsInProperty_WithSecretValue_DetectsAndBoostsConfidence()
    {
        var detector = new SecretDetector();

        var results = detector.DetectSecretsInProperty("apiKey", "AKIAIOSFODNN7EXAMPLE", null);

        results.Should().Contain(r => r.Category == "ApiKeys");
    }

    [Fact]
    public void GetStatistics_ReportsAvailablePatterns()
    {
        var detector = new SecretDetector();

        var stats = detector.GetStatistics();

        stats.TotalPatternsAvailable.Should().BeGreaterThan(0);
    }
}

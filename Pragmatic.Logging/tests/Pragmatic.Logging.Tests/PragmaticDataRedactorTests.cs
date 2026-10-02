using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Attributes;
using Pragmatic.Logging.Privacy;

namespace Pragmatic.Logging.Tests;

public class PragmaticDataRedactorTests
{
    private static PragmaticDataRedactor CreateRedactor(PragmaticDataRedactorConfiguration config)
        // Secret detection is disabled in most tests so we exercise the pattern path deterministically.
        => new(config);

    [Fact]
    public void ShouldRedact_WithExplicitRedactedFlag_ReturnsTrue()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration { EnableSecretDetection = false });

        redactor.ShouldRedact("anything", PropertyCharacteristics.Redacted).Should().BeTrue();
    }

    [Fact]
    public void ShouldRedact_WithKnownSensitiveName_IsCaseInsensitive()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration
        {
            EnableSecretDetection = false,
            SensitivePropertyNames = ["Password"],
        });

        redactor.ShouldRedact("PASSWORD", PropertyCharacteristics.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldRedact_WithNamePatternMatch_ReturnsTrue()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration
        {
            EnableSecretDetection = false,
            PropertyNamePatterns = [@"(?i).*token.*"],
        });

        redactor.ShouldRedact("AccessTokenValue", PropertyCharacteristics.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldRedact_WithNoMatch_ReturnsFalse()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration
        {
            EnableSecretDetection = false,
            SensitivePropertyNames = ["password"],
        });

        redactor.ShouldRedact("orderCount", PropertyCharacteristics.None).Should().BeFalse();
    }

    [Fact]
    public void RedactProperties_RedactsSensitiveAndPreservesOthers()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration
        {
            EnableSecretDetection = false,
            SensitivePropertyNames = ["password"],
            RedactionPlaceholder = "[REDACTED]",
        });
        var properties = new Dictionary<string, object?>
        {
            ["password"] = "hunter2",
            ["userId"] = 42,
        };

        var result = redactor.RedactProperties(properties);

        result["password"].Should().Be("[REDACTED]");
        result["userId"].Should().Be(42);
    }

    [Fact]
    public void RedactProperties_WithEmptyDictionary_ReturnsEmpty()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration { EnableSecretDetection = false });

        redactor.RedactProperties(new Dictionary<string, object?>()).Should().BeEmpty();
    }

    [Fact]
    public void RedactProperties_WithPreserveLengths_MasksWithRedactionChar()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration
        {
            EnableSecretDetection = false,
            SensitivePropertyNames = ["password"],
            PreserveLengths = true,
            RedactionChar = '*',
            MaxPreservedLength = 50,
        });
        var properties = new Dictionary<string, object?> { ["password"] = "abc" };

        var result = redactor.RedactProperties(properties);

        result["password"].Should().Be("***");
    }

    [Fact]
    public void RedactProperties_PreserveLengths_CapsAtMaxPreservedLength()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration
        {
            EnableSecretDetection = false,
            SensitivePropertyNames = ["password"],
            PreserveLengths = true,
            RedactionChar = '*',
            MaxPreservedLength = 4,
        });
        var properties = new Dictionary<string, object?> { ["password"] = "abcdefghij" };

        var result = redactor.RedactProperties(properties);

        result["password"].Should().Be("****");
    }

    [Fact]
    public void RedactMessage_WithEmpty_ReturnsEmpty()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration { EnableSecretDetection = false });

        redactor.RedactMessage(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void RedactMessage_WithMatchingPattern_ReplacesWithPlaceholder()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration
        {
            EnableSecretDetection = false,
            MessageRedactionPatterns = [@"\b\d{3}-\d{2}-\d{4}\b"],
            RedactionPlaceholder = "[REDACTED]",
        });

        var result = redactor.RedactMessage("user ssn is 123-45-6789 done");

        result.Should().NotContain("123-45-6789");
        result.Should().Be("user ssn is [REDACTED] done");
    }

    [Fact]
    public void RedactMessage_WithNoConfiguredPatternsAndNoDetector_ReturnsUnchanged()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration { EnableSecretDetection = false });

        const string message = "ordinary message 123-45-6789";
        redactor.RedactMessage(message).Should().Be(message);
    }

    [Fact]
    public void RedactProperties_WithExplicitLevelAndCategory_RedactsSensitive()
    {
        var redactor = CreateRedactor(new PragmaticDataRedactorConfiguration
        {
            EnableSecretDetection = false,
            SensitivePropertyNames = ["secret"],
            RedactionPlaceholder = "[REDACTED]",
        });
        var properties = new Dictionary<string, object?> { ["secret"] = "value" };

        var result = redactor.RedactProperties(properties, LogLevel.Warning, "Cat.Sub", userId: "u1", correlationId: "c1");

        result["secret"].Should().Be("[REDACTED]");
    }
}

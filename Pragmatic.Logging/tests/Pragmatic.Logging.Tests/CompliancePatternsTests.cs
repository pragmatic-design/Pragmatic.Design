using System.Text.RegularExpressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Logging.Privacy;

namespace Pragmatic.Logging.Tests;

public class CompliancePatternsTests
{
    // The CVV pattern was hardened to require a cvv/cvc/security-code label rather than
    // matching any bare 3-4 digit run (which produced massive false positives).
    [Theory]
    [InlineData("CVV=123")]
    [InlineData("cvc: 4567")]
    [InlineData("card verification value 321")]
    public void Cvv_WithLabelledValue_Matches(string input)
    {
        Regex.IsMatch(input, CompliancePatterns.PciDss.Cvv).Should().BeTrue();
    }

    [Theory]
    [InlineData("year 2024")]
    [InlineData("count is 999")]
    [InlineData("order 1234 shipped")]
    public void Cvv_WithBareDigits_DoesNotMatch(string input)
    {
        Regex.IsMatch(input, CompliancePatterns.PciDss.Cvv).Should().BeFalse();
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last+tag@sub.domain.org")]
    public void GdprEmail_MatchesEmailAddresses(string input)
    {
        Regex.IsMatch(input, CompliancePatterns.Gdpr.Email).Should().BeTrue();
    }

    [Fact]
    public void GdprEmail_DoesNotMatchPlainText()
    {
        Regex.IsMatch("no address present here", CompliancePatterns.Gdpr.Email).Should().BeFalse();
    }

    [Fact]
    public void GdprCreditCard_MatchesGroupedDigits()
    {
        Regex.IsMatch("pay with 4111 1111 1111 1111 now", CompliancePatterns.Gdpr.CreditCard).Should().BeTrue();
    }

    [Fact]
    public void HipaaSsn_MatchesFormattedSsn()
    {
        Regex.IsMatch("ssn 123-45-6789", CompliancePatterns.Hipaa.Ssn).Should().BeTrue();
    }

    [Fact]
    public void CreateCombined_DefaultIncludesGdprOnly()
    {
        var config = CompliancePatterns.CreateCombined();

        config.ComplianceStandards.Should().ContainSingle().Which.Should().Be("GDPR");
        config.PropertyNamePatterns.Should().NotBeEmpty();
        config.MessageContentPatterns.Should().NotBeEmpty();
    }

    [Fact]
    public void CreateCombined_WithAllStandards_AggregatesEach()
    {
        var config = CompliancePatterns.CreateCombined(includeGdpr: true, includeHipaa: true, includePciDss: true);

        config.ComplianceStandards.Should().BeEquivalentTo("GDPR", "HIPAA", "PCI-DSS");
    }

    [Fact]
    public void CreateCombined_WithNothingSelected_IsEmpty()
    {
        var config = CompliancePatterns.CreateCombined(includeGdpr: false, includeHipaa: false, includePciDss: false);

        config.ComplianceStandards.Should().BeEmpty();
        config.PropertyNamePatterns.Should().BeEmpty();
        config.MessageContentPatterns.Should().BeEmpty();
    }

    // The generic API-key pattern was hardened to require a key-like label adjacent to the
    // value, rather than matching any 32-64 char alphanumeric run (e.g. UUIDs, hashes).
    [Fact]
    public void ApiKeysGeneric_WithLabelledSecret_Matches()
    {
        const string input = "api_key=AbCdEf0123456789AbCdEf0123456789XYZ";

        Regex.IsMatch(input, SecretPatterns.ApiKeys.Generic).Should().BeTrue();
    }

    [Fact]
    public void ApiKeysGeneric_WithUnlabelledHexHash_DoesNotMatch()
    {
        // A 40-char hex digest with no key-like label is not a secret and must not be masked.
        const string input = "checksum da39a3ee5e6b4b0d3255bfef95601890afd80709";

        Regex.IsMatch(input, SecretPatterns.ApiKeys.Generic).Should().BeFalse();
    }
}

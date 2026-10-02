using Pragmatic.Testing.Assertions;

namespace Pragmatic.Audit.Tests;

/// <summary>
///     Covers the one field that could otherwise put personal data into an append-only store.
/// </summary>
/// <remarks>
///     Redaction here is a floor, not a guarantee — pattern matching cannot recognise a name. The
///     structural defence is that the entry has no general-purpose payload field at all.
/// </remarks>
public sealed class PatternAuditDetailRedactorTests
{
    private readonly PatternAuditDetailRedactor _redactor = new();

    [Theory]
    [InlineData("contact was ada@example.com", "ada@example.com")]
    [InlineData("mail: Ada.Lovelace+tag@sub.example.co.uk done", "Ada.Lovelace+tag@sub.example.co.uk")]
    public void Redact_RemovesEmailAddresses(string detail, string secret)
        => _redactor.Redact(detail).Should().NotContain(secret);

    [Fact]
    public void Redact_RemovesBearerTokens()
        => _redactor.Redact("auth Bearer eyJhbGciOiJIUzI1NiJ9.abc-_123 end")
            .Should().NotContain("eyJhbGciOiJIUzI1NiJ9");

    [Theory]
    [InlineData("card 4111 1111 1111 1111 charged")]
    [InlineData("id 12345678901234")]
    public void Redact_RemovesLongDigitRuns(string detail)
    {
        var result = _redactor.Redact(detail);

        result.Should().Contain("[redacted]");
        result.Should().NotMatchRegex(@"\d[\d ._-]{10,}\d");
    }

    [Fact]
    public void Redact_RemovesIbans()
        => _redactor.Redact("paid from GB33BUKB20201555555555 today")
            .Should().NotContain("GB33BUKB20201555555555");

    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("api_key: sk-abcdef123456")]
    [InlineData("Secret = s3cr3t")]
    public void Redact_RemovesSelfNamingSecrets(string detail)
        => _redactor.Redact(detail).Should().Contain("[redacted]");

    [Fact]
    public void Redact_LeavesOrdinaryTextAlone()
    {
        // Over-redaction is its own failure: a trail nobody can read is a trail nobody checks.
        const string detail = "Order 42 moved from Pending to Confirmed by policy DefaultEndpointPolicy";

        _redactor.Redact(detail).Should().Be(detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Redact_EmptyOrNull_IsReturnedUnchanged(string? detail)
        => _redactor.Redact(detail!).Should().Be(detail);
}

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Audit.Tests;

/// <summary>
///     The floor the trail's redactor guarantees, stated as tests rather than as a claim.
/// </summary>
/// <remarks>
///     It pins that a national identifier and a card-verification value, which the logging pipeline
///     catches, do not reach the trail in plaintext either.
///     Dates are deliberately absent — a date is personal only in context, and redacting every one
///     would empty the field that explains why an entry exists.
/// </remarks>
public class RedactionFloorProbe
{
    [Theory]
    [InlineData("ada@example.com", "email")]
    [InlineData("IT60X0542811101000000123456", "iban")]
    [InlineData("4111 1111 1111 1111", "credit card")]
    [InlineData("123-45-6789", "social security number")]
    [InlineData("+39 340 1234567", "phone")]
    [InlineData("cvv: 123", "cvv")]
    public void WhatTheTrailRedactorActuallyCatches(string input, string what)
    {
        var redacted = new PatternAuditDetailRedactor().Redact($"value {input} end");
        redacted.Should().NotContain(input, $"{what} must not reach the trail (result: {redacted})");
    }
}

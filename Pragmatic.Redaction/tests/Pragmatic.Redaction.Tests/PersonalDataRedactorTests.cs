using Pragmatic.Testing.Assertions;

namespace Pragmatic.Redaction.Tests;

/// <summary>
///     What the shared redactor catches, and — as important — what it deliberately does not.
/// </summary>
public class PersonalDataRedactorTests
{
    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    public void AnEmailAddress_IsRedacted(string email)
        => PersonalDataRedactor.Redact($"sent to {email} today").Should().NotContain(email);

    [Fact]
    public void ANationalIdentifier_IsRedacted()
    {
        // Too short for the long-digit-run pattern to see, so only its own pattern keeps it out of
        // the audit trail.
        PersonalDataRedactor.Redact("ssn 123-45-6789 on file").Should().NotContain("123-45-6789");
    }

    [Theory]
    [InlineData("cvv: 123")]
    [InlineData("CVC=4321")]
    [InlineData("security code 999")]
    public void ACardVerificationValue_IsRedacted(string input)
    {
        // Three or four digits are unrecognisable alone; the label is the only signal. Storing one is
        // forbidden outright by PCI DSS, which makes letting it through worse than most leaks.
        PersonalDataRedactor.Redact($"payment {input} end").Should().Contain(PersonalDataPatterns.Mask);
    }

    [Fact]
    public void APaymentCardNumber_IsRedacted()
        => PersonalDataRedactor.Redact("card 4111 1111 1111 1111 ok").Should().NotContain("4111");

    [Fact]
    public void AnIban_IsRedacted()
        => PersonalDataRedactor.Redact("iban IT60X0542811101000000123456 ok")
            .Should().NotContain("IT60X0542811101000000123456");

    [Fact]
    public void ABearerToken_IsRedacted()
    {
        // Bare: the scheme survives, which keeps the entry readable.
        PersonalDataRedactor.Redact("sent Bearer abc.def.ghi upstream")
            .Should().Be($"sent Bearer {PersonalDataPatterns.Mask} upstream");
    }

    [Fact]
    public void UnderAnAuthorizationLabel_TheWholePairGoes_SchemeIncluded()
    {
        // Two rules fire in turn and the output is doubly masked: the token is replaced as a bearer,
        // then the `Authorization: …` pair is replaced as a named secret — whose value pattern stops at
        // the first space, so it swallows the scheme and leaves the earlier mask behind.
        // Untidy, and left alone: a cosmetic result is not a reason to narrow a redaction rule. Pinned
        // here so the shape is a decision rather than something rediscovered in a log one day.
        var result = PersonalDataRedactor.Redact("Authorization: Bearer abc.def.ghi");

        result.Should().NotContain("abc.def.ghi");
        result.Should().Be($"Authorization={PersonalDataPatterns.Mask} {PersonalDataPatterns.Mask}");
    }

    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("api_key: sk-abcdef")]
    public void ASecretAssignedByName_IsRedacted(string input)
        => PersonalDataRedactor.Redact(input).Should().NotContain(input.Split(['=', ':'], 2)[1].Trim());

    [Fact]
    public void ADate_IsLeftAlone()
    {
        // Deliberate. A date is personal only in context — a birth date is, "locked until" is not — and
        // a pattern cannot tell them apart. Redacting every date would empty the one field that
        // explains why an entry exists, which is how a redactor stops being used at all.
        const string detail = "locked until 12/03/2026";

        PersonalDataRedactor.Redact(detail).Should().Be(detail);
    }

    [Fact]
    public void OrdinaryText_IsUntouched()
        => PersonalDataRedactor.Redact("reservation confirmed by the desk")
            .Should().Be("reservation confirmed by the desk");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingToRedact_IsReturnedAsIs(string? input)
        => PersonalDataRedactor.Redact(input).Should().Be(input);
}

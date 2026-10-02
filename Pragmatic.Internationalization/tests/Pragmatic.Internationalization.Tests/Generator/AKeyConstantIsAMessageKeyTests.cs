using Pragmatic.Testing.Assertions;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     A validation rule names its message with a <c>TKeys</c> constant, and the generated validator
///     reports that key.
/// </summary>
/// <remarks>
///     <para>
///         <c>TKeys</c> is written by the same generator that reads the rule. While it reads it,
///         the constant does not exist, the argument binds to nothing, and the rule fell back to its default
///         key — <c>validation.greaterthanorequalproperty</c> — while the compiler, running after the
///         generator, accepted the code without a word.
///     </para>
///     <para>
///         Through the constants' catalog the key is the one the constant holds. A constant the catalog does
///         not know is said (PRAG0222), not replaced.
///     </para>
/// </remarks>
public class AKeyConstantIsAMessageKeyTests : I18NGeneratorTestBase
{
    private const string Source = """
        using System;
        using Pragmatic.Internationalization.Attributes;
        using Pragmatic.Validation.Attributes;
        using Probe.Texts;

        [assembly: TranslationKeys(Namespace = "Probe.Texts")]

        namespace Probe;

        public sealed class SameYearAsAttribute(string other) : ValidationAttribute
        {
            public string Other { get; } = other;
            public override string DefaultMessageKey => "validation.sameyear";
            public override bool IsValid(object? value) => true;
        }

        public partial class Booking
        {
            public DateOnly From { get; init; }

            [GreaterThanOrEqualProperty(nameof(From), MessageKey = TKeys.Booking.EndsBeforeItStarts)]
            [SameYearAs(nameof(From), MessageKey = TKeys.Booking.SpansTwoYears)]
            public DateOnly To { get; init; }

            [Required(MessageKey = "booking.guest_is_required")]
            public string? Guest { get; init; }
        }
        """;

    private static readonly (string, string) English = ("translations/en.json", """
        { "booking": { "ends_before_it_starts": "It ends before it starts.", "spans_two_years": "One year." } }
        """);

    [Fact]
    public void ABuiltInRule_ReportsTheKeyTheConstantHolds()
        => Validator(Source)
            .Should().Contain("\"booking.ends_before_it_starts\"")
            .And.NotContain("\"validation.greaterthanorequalproperty\"");

    [Fact]
    public void ACustomRule_ReportsTheKeyTheConstantHolds()
        => Validator(Source)
            .Should().Contain("\"booking.spans_two_years\"")
            .And.NotContain("sameyearasAttr.DefaultMessageKey");

    /// <summary>The control: a key written as a string is the key it always was.</summary>
    [Fact]
    public void AKeyWrittenAsAString_IsUnchanged()
        => Validator(Source).Should().Contain("\"booking.guest_is_required\"");

    [Fact]
    public void AndTheApplicationCompiles()
        => GenerateAgainstTheRuntime(Source, English).Errors.Should().BeEmpty();

    /// <summary>A constant the catalog does not hold is said, not replaced by the default key.</summary>
    [Fact]
    public void AConstantTheCatalogDoesNotHold_IsReported()
    {
        var generated = GenerateAgainstTheRuntime(
            Source.Replace("TKeys.Booking.SpansTwoYears", "TKeys.Booking.Nowhere", StringComparison.Ordinal), English);

        generated.GeneratorDiagnostics.Select(d => d.Id).Should().Contain("PRAG0222");
    }

    /// <summary>The control for the report: every constant the catalog holds says nothing.</summary>
    [Fact]
    public void ConstantsTheCatalogHolds_ReportNothing()
        => GenerateAgainstTheRuntime(Source, English)
            .GeneratorDiagnostics.Select(d => d.Id).Should().NotContain("PRAG0222");

    private static string Validator(string source)
    {
        var generated = GenerateAgainstTheRuntime(source, English);
        var validator = generated.Sources.FirstOrDefault(s => s.Key.Contains("Booking.Validator", StringComparison.Ordinal)).Value;
        validator.Should().NotBeNull("the validator is generated at all — otherwise every assertion is about nothing");
        return validator!;
    }
}

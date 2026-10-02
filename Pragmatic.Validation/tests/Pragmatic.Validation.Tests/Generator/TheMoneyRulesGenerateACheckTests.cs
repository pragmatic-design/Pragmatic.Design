using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     The three money rules produce a check on a Pragmatic operation.
/// </summary>
/// <remarks>
///     <para>
///         The Pragmatic generator keeps an attribute only when it derives from this framework's own
///         <c>ValidationAttribute</c>. A money rule deriving from
///         <c>System.ComponentModel.DataAnnotations.ValidationAttribute</c> would get nothing emitted —
///         while the generated validator still carried a <c>// Validate {Property}</c> comment, so the
///         output would read as though a check had been produced.
///     </para>
///     <para>
///         ⚠️ Deriving from the right base is necessary and not sufficient. The generator picks
///         candidate types with a <b>syntactic</b> pass over attribute names, and that pass reads a
///         hardcoded list of simple names. A type whose only validation attribute is one the list does
///         not know is never even offered to the semantic stage.
///     </para>
/// </remarks>
public class TheMoneyRulesGenerateACheckTests : ValidationGeneratorTestBase
{
    private static readonly MetadataReference Internationalization =
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Internationalization.Types.Money));

    /// <summary>A positive-amount rule reaches the generated validator.</summary>
    [Fact]
    public void PositiveMoney_OnItsOwn_GeneratesACheck()
    {
        const string source = """
                              using Pragmatic.Internationalization.Types;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateOrderRequest
                              {
                                  [PositiveMoney]
                                  public Money Total { get; init; }
                              }
                              """;

        var result = RunGenerator(source, Internationalization);

        var generated = GetGeneratedSource(result, "Validator");
        generated.Should().NotBeNull("a type whose only rule is a money rule must still be a candidate");
        generated.Should().Contain("PositiveMoneyAttribute");
        generated.Should().Contain(".IsValid(Total)");
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     The whitelist survives as a whitelist: the allowed codes reach the generated code.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the half that could not be expressed with <c>[OneOf]</c>, which compares the whole
    ///     value — and a <see cref="Pragmatic.Internationalization.Types.Money" /> is an amount together
    ///     with its currency. The generator re-instantiates an attribute it does not know by name, so
    ///     the constructor arguments have to survive the round trip through metadata.
    /// </remarks>
    [Fact]
    public void SupportedCurrency_KeepsItsWhitelist_ThroughTheGeneratedCode()
    {
        const string source = """
                              using Pragmatic.Internationalization.Types;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreatePaymentRequest
                              {
                                  [SupportedCurrency("USD", "EUR")]
                                  public Money Amount { get; init; }
                              }
                              """;

        var result = RunGenerator(source, Internationalization);

        var generated = GetGeneratedSource(result, "Validator");
        generated.Should().NotBeNull(
            "the generator reported: {0}",
            string.Join("; ", result.Diagnostics.Select(d => d.Id + " " + d.GetMessage())));
        generated.Should().Contain("\"USD\"");
        generated.Should().Contain("\"EUR\"");
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     The control: a rule the generator will not emit still produces no check.
    /// </summary>
    /// <remarks>
    ///     Without it, "the money rules generate a check" is satisfied by a generator that emits
    ///     something for every attribute it sees, which would make the whole PRAG0210 diagnostic
    ///     meaningless. A DataAnnotations attribute must stay silent.
    /// </remarks>
    [Fact]
    public void ADataAnnotationsRule_BesideThem_StillGeneratesNothing()
    {
        const string source = """
                              using System.ComponentModel.DataAnnotations;
                              using Pragmatic.Internationalization.Types;

                              namespace TestNamespace;

                              public partial record CreateRefundRequest
                              {
                                  [Pragmatic.Validation.Attributes.PositiveMoney]
                                  public Money Total { get; init; }

                                  [EmailAddress]
                                  public string Email { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source, Internationalization);

        var generated = GetGeneratedSource(result, "Validator");
        generated.Should().NotBeNull();
        generated.Should().Contain("PositiveMoneyAttribute", "the Pragmatic rule is emitted");
        generated.Should().NotContain("EmailAddressAttribute", "the DataAnnotations one is not");
    }
}

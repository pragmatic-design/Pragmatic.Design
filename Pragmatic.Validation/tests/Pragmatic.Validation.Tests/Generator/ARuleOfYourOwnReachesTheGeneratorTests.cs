using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     A validation attribute a consumer wrote reaches the generator on its own.
/// </summary>
/// <remarks>
///     <para>
///         <c>ValidationAttribute</c>'s own documentation carries a worked example of writing one — a
///         <c>FiscalCodeAttribute</c> deriving from it — and the generator has a render path for
///         attributes it does not recognise by name, which instantiates the attribute and calls
///         <c>IsValid</c>. Between the two sits a <b>syntactic</b> pre-filter, and a type is only
///         offered to the semantic stage if it matches.
///     </para>
///     <para>
///         ⚠️ A pre-filter that read a hardcoded list of attribute simple names would never select a
///         type whose only rule is a custom one: no validator, no diagnostic, the rule simply would not
///         exist. It would appear to work as soon as the type also carried a rule the list knew — and
///         the first thing anyone writes beside a custom rule is <c>[Required]</c>, which hides it.
///     </para>
/// </remarks>
public class ARuleOfYourOwnReachesTheGeneratorTests : ValidationGeneratorTestBase
{
    private const string ARuleOfItsOwn = """
        namespace App.Rules
        {
            using Pragmatic.Validation.Attributes;

            public sealed class FiscalCodeAttribute : ValidationAttribute
            {
                public override string DefaultMessageKey => "validation.fiscalcode";

                public override bool IsValid(object? value) => value is not string s || s.Length == 16;
            }
        }
        """;

    /// <summary>A type whose only rule is a custom one still gets a validator.</summary>
    [Fact]
    public void ATypeWhoseOnlyRuleIsCustom_IsStillGenerated()
    {
        var result = RunGenerator(ARuleOfItsOwn + """

            namespace TestNamespace
            {
                public partial record RegisterRequest
                {
                    [App.Rules.FiscalCode]
                    public string Code { get; init; } = "";
                }
            }
            """);

        var generated = GetGeneratedSource(result, "Validator");
        generated.Should().NotBeNull("a rule of one's own is a rule");
        generated.Should().Contain("FiscalCodeAttribute");
        generated.Should().Contain(".IsValid(Code)");
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     With a known rule beside it the custom rule works either way, which is what would hide the hole.
    /// </summary>
    /// <remarks>
    ///     Kept as the second half of the story rather than for coverage: it passes whatever the
    ///     pre-filter reads, and its passing is precisely why the first case is needed.
    /// </remarks>
    [Fact]
    public void WithAKnownRuleBesideIt_ItWorkedAlready()
    {
        var result = RunGenerator(ARuleOfItsOwn + """

            namespace TestNamespace
            {
                using Pragmatic.Validation.Attributes;

                public partial record RegisterWithName
                {
                    [Required]
                    public string Name { get; init; } = "";

                    [App.Rules.FiscalCode]
                    public string Code { get; init; } = "";
                }
            }
            """);

        GetGeneratedSource(result, "Validator").Should().Contain("FiscalCodeAttribute");
    }

    /// <summary>
    ///     The control: a type with no validation attribute at all still generates nothing.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the one that keeps the pre-filter honest. Widening it to "any attributed
    ///     property" would make it pass; widening it to "any type" would not — and a generator that
    ///     writes a validator for every record in a solution is a worse defect than a missed custom
    ///     rule.
    /// </remarks>
    [Fact]
    public void ATypeWithNoRuleAtAll_GeneratesNothing()
    {
        var result = RunGenerator("""
            namespace TestNamespace
            {
                public partial record PlainRecord
                {
                    public string Name { get; init; } = "";
                }
            }
            """);

        GetGeneratedSource(result, "Validator").Should().BeNull(
            "nothing declared, nothing generated");
    }

    /// <summary>
    ///     The second control: an attribute that is not a validation rule does not make one appear.
    /// </summary>
    /// <remarks>
    ///     A property carrying <c>[Obsolete]</c> is attributed and validates nothing. Without this,
    ///     "any attributed property" would be indistinguishable from the correct rule, which is "any
    ///     property carrying something that derives from our base".
    /// </remarks>
    [Fact]
    public void AnAttributeThatIsNotARule_GeneratesNothing()
    {
        var result = RunGenerator("""
            namespace TestNamespace
            {
                public partial record Annotated
                {
                    [System.Obsolete]
                    public string Name { get; init; } = "";
                }
            }
            """);

        GetGeneratedSource(result, "Validator").Should().BeNull(
            "an attribute is not a rule just by being an attribute");
    }
}

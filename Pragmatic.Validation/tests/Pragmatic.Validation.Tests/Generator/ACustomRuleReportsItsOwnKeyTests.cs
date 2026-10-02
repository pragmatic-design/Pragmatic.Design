using System.Runtime.CompilerServices;
using Pragmatic.SourceGenerator.Features.Validation.Models;
using Pragmatic.SourceGenerator.Features.Validation.Transforms;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     A rule reports the message key it declares, and nobody else's.
/// </summary>
/// <remarks>
///     <para>
///         Measured on a consumer: <c>[NotFutureYear]</c> declared
///         <c>DefaultMessageKey => "validation.not_future_year"</c> and the wire said
///         <c>validation.invalid</c>. The generator took the key from a hand-written table indexed by
///         the kinds it knows, and every kind it does not know — every custom rule — fell through to
///         <c>validation.invalid</c>. The attribute's own answer, the abstract member every rule has to
///         implement, was never read.
///     </para>
///     <para>
///         These run the generated validator instead of reading it: the key is what reaches
///         <see cref="Types.ValidationIssue.MessageKey" />.
///     </para>
/// </remarks>
public class ACustomRuleReportsItsOwnKeyTests : ValidationGeneratorTestBase
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

    [Fact]
    public void Validate_ACustomRuleWithoutAMessageKey_ReportsTheRulesDefaultKey()
    {
        var issue = ValidateOneInvalidCode("[App.Rules.FiscalCode]");

        issue.Should().Be("validation.fiscalcode");
    }

    [Fact]
    public void Validate_ACustomRuleWithAMessageKey_ReportsTheKeyItWasGiven()
    {
        var issue = ValidateOneInvalidCode("[App.Rules.FiscalCode(MessageKey = \"custom.fiscal\")]");

        issue.Should().Be("custom.fiscal");
    }

    /// <summary>
    ///     The table the generator keeps for the rules it inlines is a copy of what the attributes
    ///     say. This checks the copy against the original, so a new rule — or a renamed key — fails
    ///     here instead of on the wire.
    /// </summary>
    [Fact]
    public void EveryRuleTheGeneratorInlines_UsesTheKeyTheAttributeDeclares()
    {
        var rules = typeof(ValidationAttribute).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(ValidationAttribute)))
            .ToList();
        rules.Should().NotBeEmpty();

        var mismatches = new List<string>();
        var inlined = 0;
        foreach (var rule in rules)
        {
            var kind = ValidatableTransform.GetValidationKind(rule.FullName!);
            if (kind == ValidationKind.Unknown)
                continue;

            inlined++;
            // The keys are constant getters; an uninitialised instance answers them without a
            // constructor, and every rule's constructor takes different arguments.
            var declared = ((ValidationAttribute)RuntimeHelpers.GetUninitializedObject(rule)).DefaultMessageKey;
            var generated = ValidationMessageKeys.ForKind(kind);
            if (generated != declared)
                mismatches.Add($"{rule.Name}: generator '{generated}', attribute '{declared}'");
        }

        inlined.Should().BeGreaterThan(0, "a check that compared nothing would pass");
        mismatches.Should().BeEmpty();
    }

    private static string ValidateOneInvalidCode(string rule)
    {
        var result = RunGenerator(ARuleOfItsOwn + $$"""

            namespace TestNamespace
            {
                public partial record RegisterRequest
                {
                    {{rule}}
                    public string Code { get; init; } = "";
                }
            }
            """);

        var assembly = EmitAndLoad(result);
        var request = Activator.CreateInstance(assembly.GetType("TestNamespace.RegisterRequest")!)!;
        request.GetType().GetProperty("Code")!.SetValue(request, "too short");

        var error = ((ISyncValidator)request).Validate();

        error.Issues.Should().HaveCount(1);
        return error.Issues[0].MessageKey;
    }
}

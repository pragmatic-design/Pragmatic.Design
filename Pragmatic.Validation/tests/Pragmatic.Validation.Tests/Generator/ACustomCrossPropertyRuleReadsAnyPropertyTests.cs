using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     A custom cross-property attribute reads its sibling through the generated
///     <c>GetPropertyValue</c>, and the sibling is there whether or not it carries a rule of its own.
/// </summary>
/// <remarks>
///     The switch listed only the validated properties, so "same year as <c>From</c>" read
///     <c>null</c> for a rule-less <c>From</c> — and a null sibling means "the rule does not apply". Valid,
///     always, with nothing to say so.
/// </remarks>
public class ACustomCrossPropertyRuleReadsAnyPropertyTests : ValidatorGeneratorTestBase
{
    private const string Source = """
                                  using System;
                                  using Pragmatic.Validation;
                                  using Pragmatic.Validation.Attributes;

                                  namespace TestNamespace;

                                  public sealed class SameYearAsAttribute(string other) : ValidationAttribute
                                  {
                                      public string Other { get; } = other;
                                      public override string DefaultMessageKey => "validation.sameyear";
                                      public override bool RequiresInstance => true;
                                      public override bool IsValid(object? value) => true;
                                      public override bool IsValid(object? value, object instance)
                                          => ((IPropertyValueProvider)instance).GetPropertyValue(Other) is not DateOnly other
                                             || value is not DateOnly day
                                             || other.Year == day.Year;
                                  }

                                  public partial record Period
                                  {
                                      public DateOnly From { get; init; }

                                      [SameYearAs(nameof(From))]
                                      public DateOnly To { get; init; }

                                      public int this[int index] => index;

                                      public string WriteOnly { set { } }
                                  }
                                  """;

    [Fact]
    public void TheSibling_IsReadable_ThoughItCarriesNoRule()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        GetGeneratedSource(result, "Period.Validator")!
            .Should().Contain("nameof(From) => From,", "the rule on To reads it");
    }

    /// <summary>What cannot be read by name is not offered: an indexer, a property with no getter.</summary>
    [Fact]
    public void AnIndexerOrAWriteOnlyProperty_IsNotOffered()
        => GetGeneratedSource(RunGenerator(Source), "Period.Validator")!
            .Should().NotContain("WriteOnly")
            .And.NotContain("this[");
}

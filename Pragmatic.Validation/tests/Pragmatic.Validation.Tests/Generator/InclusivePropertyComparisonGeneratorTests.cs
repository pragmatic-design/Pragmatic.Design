using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     <c>[GreaterThanOrEqualProperty]</c> and <c>[LessThanOrEqualProperty]</c> are generated as direct
///     comparisons, like the strict pair — the equal value passes.
/// </summary>
/// <remarks>
///     A period that ends on the day it starts could not be declared: the strict form refuses it, and Time
///     off checked <c>To &lt; From</c> by hand inside the operation.
/// </remarks>
public class InclusivePropertyComparisonGeneratorTests : ValidatorGeneratorTestBase
{
    private const string Source = """
                                  using System;
                                  using Pragmatic.Validation.Attributes;

                                  namespace TestNamespace;

                                  public partial record Period
                                  {
                                      public DateOnly From { get; init; }

                                      [GreaterThanOrEqualProperty(nameof(From))]
                                      public DateOnly To { get; init; }

                                      public int Ceiling { get; init; }

                                      [LessThanOrEqualProperty(nameof(Ceiling))]
                                      public int Used { get; init; }
                                  }
                                  """;

    [Fact]
    public void GreaterThanOrEqualProperty_RefusesOnlyALowerValue()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        GetGeneratedSource(result, "Period.Validator")!
            .Should().Contain("CrossPropertyComparison.Compare(To, From) is int cmpGte")
            .And.Contain("< 0")
            .And.Contain("validation.greaterthanorequalproperty");
    }

    [Fact]
    public void LessThanOrEqualProperty_RefusesOnlyAHigherValue()
    {
        var validator = GetGeneratedSource(RunGenerator(Source), "Period.Validator")!;

        validator.Should().Contain("CrossPropertyComparison.Compare(Used, Ceiling) is int cmpLte")
            .And.Contain("> 0")
            .And.Contain("validation.lessthanorequalproperty");
    }

    /// <summary>Known to the generator, so not re-instantiated at run time as an unknown attribute.</summary>
    [Fact]
    public void TheRules_AreGeneratedNotInterpreted()
        => GetGeneratedSource(RunGenerator(Source), "Period.Validator")!
            .Should().NotContain("Runtime validation via");

    [Theory]
    [InlineData("GreaterThanOrEqualProperty")]
    [InlineData("LessThanOrEqualProperty")]
    public void ANameThatDoesNotExist_IsPrag0203Alone(string attribute)
    {
        var source = $$"""
                       using Pragmatic.Validation.Attributes;

                       namespace TestNamespace;

                       public partial record Band
                       {
                           [{{attribute}}("Nonexistent")]
                           public int Value { get; init; }
                       }
                       """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0203").Should().BeTrue();
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>Two properties of different types are the same warning as for the strict pair.</summary>
    [Fact]
    public void DifferentTypes_ArePrag0209()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record Band
                              {
                                  public long Floor { get; init; }

                                  [GreaterThanOrEqualProperty(nameof(Floor))]
                                  public int Value { get; init; }
                              }
                              """;

        HasDiagnostic(RunGenerator(source), "PRAG0209").Should().BeTrue();
    }
}

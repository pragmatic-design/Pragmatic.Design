using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     A length rule on an array compares its <c>Length</c> (#712).
/// </summary>
/// <remarks>
///     An array is a collection, and the rule emitted <c>.Count</c> for every collection: on an array that is LINQ's method, so
///     <c>Scan.Count &gt; 3000</c> was CS0019 in the generated validator, for every <c>[MaxLength]</c> on a <c>byte[]</c>, which is how a
///     binary column declares its length.
/// </remarks>
public class ALengthRuleOnAnArrayTests : ValidationGeneratorTestBase
{
    [Fact]
    public void MaxLengthOnAByteArray_ComparesItsLength_AndCompiles()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record UploadScan
                              {
                                  [MaxLength(3000)]
                                  public byte[] Scan { get; init; } = [];
                              }
                              """;

        var result = RunGenerator(source);

        GetGeneratedSource(result, "Validator").Should().Contain("Scan.Length > 3000");
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>The control: a list keeps its <c>Count</c>, which is a property there.</summary>
    [Fact]
    public void MaxLengthOnAList_StillComparesItsCount()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record TagSet
                              {
                                  [MaxLength(5)]
                                  public List<string> Tags { get; init; } = [];
                              }
                              """;

        var result = RunGenerator(source);

        GetGeneratedSource(result, "Validator").Should().Contain("Tags.Count > 5");
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }
}

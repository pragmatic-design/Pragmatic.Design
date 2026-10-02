// Pragmatic.Result.Tests - Generator Tests
// Tests that the ResultSourceGenerator produces valid output

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Result.SourceGenerator;
using Xunit;

namespace Pragmatic.Result.Tests.Generator;

public class GeneratorTests : ResultGeneratorTestBase
{
    [Fact]
    public void Generator_ProducesExpectedNumberOfFiles()
    {
        var result = RunResultGenerator();

        // Should generate Result3-Result9 (7 files) + VoidResult2-VoidResult8 (7 files) = 14 files
        Assert.Equal(14, result.GeneratedTrees.Length);
    }

    [Fact]
    public void Generator_ProducesNoDiagnostics()
    {
        var result = RunResultGenerator();
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Generator_Result3Variant_EmitsZeroAllocStructWithMatchAndTryGetError()
    {
        var result = RunResultGenerator();

        var source = GetGeneratedSource(result, "Result3_Generated");

        Assert.NotNull(source);
        Assert.Contains("public readonly struct Result<", source);
        Assert.Contains("namespace Pragmatic.Result", source);
        Assert.Contains("Match", source);
        Assert.Contains("TryGetError1", source);
        Assert.Contains("TryGetError2", source);
    }

    [Fact]
    public void Generator_VoidResult2Variant_EmitsZeroAllocStructWithTryGetError()
    {
        var result = RunResultGenerator();

        var source = GetGeneratedSource(result, "VoidResult2_Generated");

        Assert.NotNull(source);
        Assert.Contains("public readonly struct VoidResult<", source);
        Assert.Contains("namespace Pragmatic.Result", source);
        Assert.Contains("TryGetError1", source);
        Assert.Contains("TryGetError2", source);
    }

    [Fact]
    public void Generator_OnlyRunsForPragmaticResultAssembly()
    {
        // Create compilation with different assembly name — generator must not activate
        var compilation = CSharpCompilation.Create(
            "SomeOtherAssembly",
            [CSharpSyntaxTree.ParseText("")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ResultSourceGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out _, out _);

        var runResult = driver.GetRunResult();

        Assert.Empty(runResult.GeneratedTrees);
    }
}
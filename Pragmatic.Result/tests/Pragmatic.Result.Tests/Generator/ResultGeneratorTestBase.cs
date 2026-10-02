// Pragmatic.Result.Tests - Generator Test Base
// Wrapper for ResultSourceGenerator (only runs for "Pragmatic.Result" assembly).

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Result.SourceGenerator;
using Pragmatic.SourceGen.Testing;

namespace Pragmatic.Result.Tests.Generator;

/// <summary>
///     Base class for ResultSourceGenerator tests.
///     NOTE: The generator only activates when the assembly name is "Pragmatic.Result".
/// </summary>
public abstract class ResultGeneratorTestBase
{
    /// <summary>
    ///     Runs the ResultSourceGenerator on an empty compilation named "Pragmatic.Result".
    ///     The generator activates only for its own assembly and needs no source input.
    /// </summary>
    protected static SourceGenRunResult RunResultGenerator()
    {
        var compilation = CSharpCompilation.Create(
            "Pragmatic.Result",
            [CSharpSyntaxTree.ParseText("")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ResultSourceGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        return new SourceGenRunResult(runResult, outputCompilation, diagnostics);
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name (case-insensitive contains).
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintName)
        => GeneratorTestHelper.GetGeneratedSource(result, hintName);

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);
}

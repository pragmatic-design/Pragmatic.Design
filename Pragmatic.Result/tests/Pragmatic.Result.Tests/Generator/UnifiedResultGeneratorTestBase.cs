// Pragmatic.Result.Tests - Unified Generator Test Base
// Runs the unified PragmaticSourceGenerator (Features/Result) against user source.

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Result.Tests.Generator;

/// <summary>
///     Base class for tests that exercise the unified <see cref="PragmaticSourceGenerator" />
///     Result feature (WriteExtensions generation on error types with custom properties).
/// </summary>
public abstract class UnifiedResultGeneratorTestBase
{
    /// <summary>
    ///     Runs the unified generator on the provided source with the Result feature active
    ///     (references bring in <c>Pragmatic.Result.IError</c> so FeatureDetector.HasResult is true).
    /// </summary>
    protected static SourceGenRunResult RunUnifiedGenerator(string source)
    {
        var references = new[]
        {
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Error)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.IError)),
            // System.Text.Json — lets test sources use [JsonIgnore] (the WriteExtensions opt-out, #28).
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute))
        };

        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
        => GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);

    protected static bool HasCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.HasCompilationErrors(result);

    protected static IEnumerable<Diagnostic> GetCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result);
}

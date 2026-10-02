// Pragmatic.Patch.Tests - Generator Test Base
// Thin wrapper over shared GeneratorTestHelper with Patch-specific references.

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Patch.Tests.Generator;

/// <summary>
///     Base class for PragmaticSourceGenerator tests.
///     Provides helper methods to run the generator and verify output.
/// </summary>
public abstract class PatchGeneratorTestBase
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator on the provided source code.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, GetPatchReferences());
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name (case-insensitive contains).
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintName)
        => GeneratorTestHelper.GetGeneratedSource(result, hintName);

    /// <summary>
    ///     Checks if the compilation has any errors after generation.
    /// </summary>
    protected static bool HasCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.HasCompilationErrors(result);

    /// <summary>
    ///     Gets generator-specific diagnostics (PRAG2200-2249).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG22");

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);

    private static MetadataReference[] GetPatchReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<Optional<int>>(),
            GeneratorTestHelper.FromType<System.Text.Json.Serialization.JsonConverter<int>>()
        ];
    }
}

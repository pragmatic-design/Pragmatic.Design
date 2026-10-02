using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Patch;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Base class for PragmaticSourceGenerator tests.
///     Provides helper methods to run the generator and verify output.
/// </summary>
public abstract class MappingGeneratorTestBase
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator on the provided source code.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = GetMappingReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name.
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
    {
        return GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);
    }

    /// <summary>
    ///     Gets all generated sources as a dictionary for snapshot testing.
    /// </summary>
    protected static Dictionary<string, string> GetGeneratedSourcesAsDictionary(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
    }

    /// <summary>
    ///     Checks if the compilation has any errors after generation.
    /// </summary>
    protected static bool HasCompilationErrors(SourceGenRunResult result)
    {
        return GeneratorTestHelper.HasCompilationErrors(result);
    }

    /// <summary>
    ///     Gets compilation errors after generation.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetCompilationErrors(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetCompilationErrors(result);
    }

    /// <summary>
    ///     Gets compilation warnings after generation.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetCompilationWarnings(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetCompilationWarnings(result);
    }

    /// <summary>
    ///     Gets generator-specific diagnostics (PRAG0300-0399).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG03");
    }

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
    {
        return GeneratorTestHelper.HasDiagnostic(result, diagnosticId);
    }

    /// <summary>
    ///     Gets all diagnostics with a specific ID.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetDiagnosticsById(SourceGenRunResult result, string diagnosticId)
    {
        return GeneratorTestHelper.GetDiagnosticsById(result, diagnosticId);
    }

    /// <summary>
    ///     Checks if there are no generated files (generator skipped due to error).
    /// </summary>
    protected static bool HasNoGeneratedFiles(SourceGenRunResult result)
    {
        return !result.HasGeneratedFiles;
    }

    private static MetadataReference[] GetMappingReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
            // Pragmatic.Ensure.Ensure is static, so we use FromTypeAssembly
            GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
            // Pragmatic.Persistence.Patch for [Patch<T>] integration tests
            GeneratorTestHelper.FromType<PatchAttribute<object>>(),
            // Pragmatic.Persistence for [Entity] and [Relation.*]: a mapping that flattens through a
            // GENERATED navigation cannot be written without them, and that is the half a
            // hand-declared property cannot reproduce.
            GeneratorTestHelper.FromType<Persistence.Entity.EntityAttribute>(),
            // System.Collections.Immutable for ImmutableArray/ImmutableList DTO collections
            GeneratorTestHelper.FromType<System.Collections.Immutable.ImmutableArray<object>>()
        ];
    }
}
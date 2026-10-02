using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
/// Base class for Configuration generator tests.
/// Thin wrapper over shared GeneratorTestHelper with Configuration-specific references.
/// </summary>
public abstract class ConfigurationGeneratorTestBase
{
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = GetConfigurationReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    protected static SourceGenRunResult RunGeneratorWithComposition(string source)
    {
        var references = GetConfigurationReferences()
            .Concat(GetCompositionReferences())
            .ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
        => GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);

    protected static Dictionary<string, string> GetGeneratedSourcesAsDictionary(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

    protected static bool HasCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.HasCompilationErrors(result);

    protected static IEnumerable<Diagnostic> GetCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result);

    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG20");

    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);

    private static MetadataReference[] GetConfigurationReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<ConfigurationAttribute>(),
            // DataAnnotations for [Required], [Range], etc. in test source code
            GeneratorTestHelper.FromType<System.ComponentModel.DataAnnotations.RequiredAttribute>(),
        ];
    }

    private static MetadataReference[] GetCompositionReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<Pragmatic.Composition.Attributes.PragmaticMetadataAttribute>(),
        ];
    }
}

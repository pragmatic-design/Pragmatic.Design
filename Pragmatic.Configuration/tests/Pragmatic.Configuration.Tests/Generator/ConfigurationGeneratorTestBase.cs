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

    /// <summary>
    ///     With the assemblies the generated registration calls into, so the output compiles: what
    ///     <see cref="RunGenerator" /> leaves out, because most tests read the generated text and never compile it.
    /// </summary>
    protected static SourceGenRunResult RunGeneratorToCompile(string source)
    {
        MetadataReference[] runtime =
        [
            GeneratorTestHelper.FromType<Pragmatic.Configuration.ISensitiveKeyClassifier>(),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Configuration.IConfiguration>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.Configuration.ConfigurationBinder)),
            GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Options.OptionsBuilder<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.Options.ConfigurationChangeTokenSource<>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.Options.DataAnnotationValidateOptions<>)),
        ];
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, [.. GetConfigurationReferences(), .. runtime]);
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

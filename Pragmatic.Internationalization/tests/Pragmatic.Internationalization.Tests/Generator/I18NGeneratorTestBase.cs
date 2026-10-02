// Pragmatic.Internationalization.Tests - Generator Test Base
// Thin wrapper for PragmaticSourceGenerator (unified) with AdditionalFiles support.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     Base class for TranslationKeys generator tests.
///     Provides helpers to run the unified PragmaticSourceGenerator with JSON AdditionalFiles and verify output.
/// </summary>
public abstract class I18NGeneratorTestBase
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator with in-memory JSON files as AdditionalTexts.
    /// </summary>
    /// <param name="files">Tuples of (path, jsonContent) representing translation files.</param>
    protected static SourceGenRunResult RunGeneratorWithJson(params (string Path, string Content)[] files)
    {
        var additionalTexts = files
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Content))
            .ToImmutableArray();

        // Need Pragmatic.Internationalization reference so FeatureDetector enables HasI18n
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            GeneratorTestHelper.FromTypeAssembly(typeof(Internationalization.Types.CultureCode))
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText("")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PragmaticSourceGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: additionalTexts);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        return new SourceGenRunResult(runResult, outputCompilation, diagnostics);
    }

    /// <summary>
    ///     Same, with C# source in the compilation — needed by anything that inspects declarations
    ///     rather than only the translation files.
    /// </summary>
    /// <remarks>
    ///     The tree is parsed with a path: <c>LocationInfo.From</c> returns null when the file path
    ///     is empty, which silently drops the location off every diagnostic.
    /// </remarks>
    protected static SourceGenRunResult RunGeneratorWithSource(
        string source,
        params (string Path, string Content)[] files)
    {
        var additionalTexts = files
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Content))
            .ToImmutableArray();

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            GeneratorTestHelper.FromTypeAssembly(typeof(Internationalization.Types.CultureCode))
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "TestSource.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new PragmaticSourceGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return new SourceGenRunResult(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    /// <summary>
    ///     Same, compiled against every assembly the test process runs with, and answered as what an
    ///     application sees: the errors of the compilation after generation, the generated files by hint
    ///     name, and the generator's own diagnostics.
    /// </summary>
    /// <remarks>
    ///     The reduced reference set of <see cref="RunGeneratorWithSource" /> cannot compile the generated
    ///     <c>T</c>; a test that asks whether the application compiles needs the whole runtime.
    /// </remarks>
    protected static GeneratedApplication GenerateAgainstTheRuntime(
        string source,
        params (string Path, string Content)[] files)
    {
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source, path: "Probe.cs")],
            RuntimeReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new PragmaticSourceGenerator().AsSourceGenerator()],
            additionalTexts: [.. files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Content))]);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        return new GeneratedApplication(
            [.. output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())],
            driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources)
                .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal),
            driver.GetRunResult().Diagnostics);
    }

    /// <summary>What <see cref="GenerateAgainstTheRuntime" /> answers.</summary>
    protected sealed record GeneratedApplication(
        ImmutableArray<string> Errors,
        IReadOnlyDictionary<string, string> Sources,
        ImmutableArray<Diagnostic> GeneratorDiagnostics);

    private static readonly MetadataReference[] RuntimeReferences =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    /// <summary>
    ///     Runs the generator and returns all diagnostics emitted during generation.
    ///     Convenience overload for diagnostic-focused tests.
    /// </summary>
    protected static ImmutableArray<Diagnostic> RunGeneratorAndGetDiagnostics(
        params (string Path, string Content)[] files)
    {
        var result = RunGeneratorWithJson(files);
        // Generator-emitted diagnostics (via context.ReportDiagnostic) appear in RunResult.Diagnostics
        return result.RunResult.Diagnostics;
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name (case-insensitive contains).
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintName)
        => GeneratorTestHelper.GetGeneratedSource(result, hintName);

    /// <summary>
    ///     Gets generator-specific diagnostics (PRAG1800-1899).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => result.RunResult.Diagnostics.Where(d => d.Id.StartsWith("PRAG18", StringComparison.Ordinal));

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => result.RunResult.Diagnostics.Any(d => d.Id == diagnosticId);

    /// <summary>
    ///     In-memory implementation of <see cref="AdditionalText" /> for testing.
    /// </summary>
    protected sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText? GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content);
    }
}

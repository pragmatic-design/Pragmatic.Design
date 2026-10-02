// =============================================================================
// Pragmatic.Design - Generator Test Base
// Base class and helpers for testing Source Generators
// =============================================================================

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.Testing;

/// <summary>
///     Base class for testing IIncrementalGenerator implementations.
///     Provides helper methods for running generators and verifying output.
/// </summary>
public abstract class GeneratorTestBase<TGenerator> where TGenerator : IIncrementalGenerator, new()
{
    /// <summary>
    ///     Runs the generator on the provided source code.
    /// </summary>
    protected GeneratorDriverRunResult RunGenerator(string source, params Assembly[] additionalReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };

        // Add runtime references
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));

        // Add additional references
        foreach (var assembly in additionalReferences)
            references.Add(MetadataReference.CreateFromFile(assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new TGenerator();

        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return driver.GetRunResult();
    }

    /// <summary>
    ///     Runs the generator and returns only the generated sources.
    /// </summary>
    protected ImmutableArray<GeneratedSourceResult> GetGeneratedSources(string source)
    {
        var result = RunGenerator(source);
        return result.Results.SelectMany(r => r.GeneratedSources).ToImmutableArray();
    }

    /// <summary>
    ///     Runs the generator and returns diagnostics.
    /// </summary>
    protected ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        var result = RunGenerator(source);
        return result.Diagnostics;
    }

    /// <summary>
    ///     Gets the generated source text for a specific hint name.
    /// </summary>
    protected string? GetGeneratedSource(string source, string hintName)
    {
        var sources = GetGeneratedSources(source);
        var match = sources.FirstOrDefault(s => s.HintName == hintName);
        return match.SourceText?.ToString();
    }

    /// <summary>
    ///     Verifies the generator produces no diagnostics.
    /// </summary>
    protected void AssertNoDiagnostics(string source)
    {
        var diagnostics = GetDiagnostics(source);
        if (diagnostics.Length > 0)
        {
            var messages = string.Join("\n", diagnostics.Select(d => d.ToString()));
            throw new Exception($"Expected no diagnostics but found:\n{messages}");
        }
    }

    /// <summary>
    ///     Verifies the generator produces a specific diagnostic.
    /// </summary>
    protected void AssertDiagnostic(string source, string diagnosticId)
    {
        var diagnostics = GetDiagnostics(source);
        if (!diagnostics.Any(d => d.Id == diagnosticId))
        {
            var found = diagnostics.Length == 0
                ? "no diagnostics"
                : string.Join(", ", diagnostics.Select(d => d.Id));
            throw new Exception($"Expected diagnostic '{diagnosticId}' but found: {found}");
        }
    }
}

/// <summary>
///     Extensions for GeneratorDriverRunResult to simplify testing.
/// </summary>
public static class GeneratorTestExtensions
{
    /// <summary>
    ///     Gets all generated source texts as a dictionary keyed by hint name.
    /// </summary>
    public static Dictionary<string, string> GetGeneratedSourcesAsDictionary(this GeneratorDriverRunResult result)
    {
        return result.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString());
    }

    /// <summary>
    ///     Asserts that a specific file was generated.
    /// </summary>
    public static void AssertGeneratedFile(this GeneratorDriverRunResult result, string hintName)
    {
        var sources = result.Results.SelectMany(r => r.GeneratedSources);
        if (!sources.Any(s => s.HintName == hintName))
        {
            var found = string.Join(", ", sources.Select(s => s.HintName));
            throw new Exception($"Expected generated file '{hintName}' but found: {found}");
        }
    }

    /// <summary>
    ///     Gets error diagnostics only.
    /// </summary>
    public static ImmutableArray<Diagnostic> GetErrors(this GeneratorDriverRunResult result)
    {
        return result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
    }

    /// <summary>
    ///     Gets warning diagnostics only.
    /// </summary>
    public static ImmutableArray<Diagnostic> GetWarnings(this GeneratorDriverRunResult result)
    {
        return result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToImmutableArray();
    }
}
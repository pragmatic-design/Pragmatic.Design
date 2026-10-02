using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Analyzers.Tests.Suppressors;

/// <summary>
///     Runs a <see cref="DiagnosticSuppressor" /> against an in-memory compilation and returns every
///     diagnostic with its suppression state resolved.
///     The standard analyzer verifiers assert on *reported* diagnostics; a suppressor changes
///     <see cref="Diagnostic.IsSuppressed" /> instead, so the tests inspect that flag directly.
/// </summary>
internal static class SuppressorTestHarness
{
    /// <summary>File path marking a source as SG output (Roslyn treats <c>.g.cs</c> as generated code).</summary>
    public const string GeneratedPath = "Sample.Feature.g.cs";

    /// <summary>File path marking a hand-written source.</summary>
    public const string HandWrittenPath = "Sample.cs";

    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
        DiagnosticSuppressor suppressor,
        DiagnosticAnalyzer? producer,
        params (string Path, string Source)[] files)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = files
            .Select(file => CSharpSyntaxTree.ParseText(file.Source, parseOptions, file.Path))
            .ToArray();

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"))
        };

        var compilation = CSharpCompilation.Create(
            "SuppressorTests",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                // Without this a suppressed diagnostic simply vanishes from the result and the
                // tests could not tell "suppressed" from "never reported".
                reportSuppressedDiagnostics: true));

        var analyzers = producer is null
            ? ImmutableArray.Create<DiagnosticAnalyzer>(suppressor)
            : ImmutableArray.Create(producer, (DiagnosticAnalyzer)suppressor);

        var analyzerOptions = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: null,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: true);

        var withAnalyzers = compilation.WithAnalyzers(analyzers, analyzerOptions);
        return await withAnalyzers.GetAllDiagnosticsAsync();
    }

    /// <summary>
    ///     Single diagnostic with <paramref name="id" /> whose message mentions <paramref name="member" />,
    ///     optionally restricted to one source file.
    /// </summary>
    public static Diagnostic Single(
        ImmutableArray<Diagnostic> diagnostics, string id, string member, string? path = null)
    {
        var matches = diagnostics
            .Where(d => d.Id == id)
            .Where(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains(member))
            .Where(d => path is null || d.Location.SourceTree?.FilePath == path)
            .ToArray();

        if (matches.Length != 1)
        {
            var seen = string.Join(
                System.Environment.NewLine,
                diagnostics.Select(d =>
                    $"  {d.Id} @ {d.Location.SourceTree?.FilePath} suppressed={d.IsSuppressed} :: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"));
            throw new Xunit.Sdk.XunitException(
                $"Expected exactly one '{id}' mentioning '{member}'{(path is null ? "" : $" in '{path}'")}, found {matches.Length}.{System.Environment.NewLine}All diagnostics:{System.Environment.NewLine}{seen}");
        }

        return matches[0];
    }
}

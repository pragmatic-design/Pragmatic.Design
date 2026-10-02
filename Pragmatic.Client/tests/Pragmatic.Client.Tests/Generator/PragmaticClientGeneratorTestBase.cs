using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Pragmatic.Client.SourceGenerator;

namespace Pragmatic.Client.Tests.Generator;

/// <summary>
///     Test base for PragmaticClientGenerator. Provides helpers to run the SG with
///     a manifest JSON as AdditionalText (Mode A) or as assembly attribute (Mode B).
/// </summary>
public abstract class PragmaticClientGeneratorTestBase
{
    protected static GeneratorDriverRunResult RunGeneratorWithManifest(string manifestJson)
        => RunGeneratorWithManifest(manifestJson, boundaryFilter: null);

    /// <param name="manifestJson">The API manifest supplied to the generator as an AdditionalText.</param>
    /// <param name="boundaryFilter">Value of the <c>PragmaticClientBoundaries</c> MSBuild property, or null.</param>
    protected static GeneratorDriverRunResult RunGeneratorWithManifest(string manifestJson, string? boundaryFilter)
        => RunGeneratorWithManifests([manifestJson], boundaryFilter);

    /// <summary>
    ///     Runs the generator over several manifests at once, the way a client project referencing more than
    ///     one boundary assembly does. Needed to cover types reachable from several manifests.
    /// </summary>
    protected static GeneratorDriverRunResult RunGeneratorWithManifests(
        string[] manifestJson, string? boundaryFilter = null)
    {
        var compilation = CreateCompilation();
        var generator = new PragmaticClientGenerator();

        // The name must END WITH "manifest.json" — that is how the generator selects AdditionalFiles.
        var additionalTexts = manifestJson
            .Select((json, i) => (AdditionalText)new InMemoryAdditionalText($"boundary-{i}-manifest.json", json))
            .ToArray();

        var driver = CSharpGeneratorDriver
            .Create(
                [generator.AsSourceGenerator()],
                additionalTexts: additionalTexts,
                optionsProvider: new TestOptionsProvider(boundaryFilter))
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult();
    }

    /// <summary>Supplies <c>build_property.PragmaticClientBoundaries</c> to the generator.</summary>
    private sealed class TestOptionsProvider(string? boundaryFilter) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestOptions(boundaryFilter);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class TestOptions(string? boundaryFilter) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.PragmaticClientBoundaries" && boundaryFilter is not null)
                {
                    value = boundaryFilter;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }

    /// <summary>The diagnostics the generator reported (PRAG2300-2349).</summary>
    protected static IReadOnlyList<Diagnostic> GetDiagnostics(GeneratorDriverRunResult result)
        => result.Diagnostics;

    protected static Dictionary<string, string> GetGeneratedSources(GeneratorDriverRunResult result)
    {
        return result.GeneratedTrees
            .ToDictionary(
                t => Path.GetFileName(t.FilePath),
                t => t.GetText().ToString());
    }

    protected static string? GetGeneratedSource(GeneratorDriverRunResult result, string hintNameContains)
    {
        return result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(hintNameContains))?
            .GetText().ToString();
    }

    private static CSharpCompilation CreateCompilation()
    {
        // Minimal compilation with required references for generated code
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(HttpClient).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Pragmatic.Result.IError).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ApiError).Assembly.Location),
        };

        // Add runtime assemblies needed for compilation
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in new[] { "System.Runtime.dll", "System.Net.Http.dll", "System.Text.Json.dll",
            "System.Collections.dll", "System.Linq.dll", "System.Threading.dll",
            "netstandard.dll", "System.Net.Http.Json.dll" })
        {
            var path = Path.Combine(runtimeDir, dll);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return CSharpCompilation.Create(
            "Showcase.BlazorClient",
            syntaxTrees: Array.Empty<SyntaxTree>(),
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>In-memory AdditionalText for test manifests.</summary>
    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText? GetText(CancellationToken ct = default)
            => SourceText.From(text);
    }
}

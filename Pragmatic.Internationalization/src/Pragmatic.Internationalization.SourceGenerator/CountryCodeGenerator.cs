// =============================================================================
// Pragmatic.Internationalization - CountryCodeGenerator
// Source generator for ISO 3166-1 country code static properties
// =============================================================================

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

using Pragmatic.Internationalization.SourceGenerator.Models;
using Pragmatic.Internationalization.SourceGenerator.Templates;

namespace Pragmatic.Internationalization.SourceGenerator;

/// <summary>
///     Generates static CountryCode properties from embedded JSON data.
/// </summary>
[Generator]
public sealed class CountryCodeGenerator : IIncrementalGenerator
{
    private const string MarkerFileName = ".generate-countries";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Only generate if the marker file is present
        var hasMarker = context.AdditionalTextsProvider
            .Select((file, _) => Path.GetFileName(file.Path))
            .Collect()
            .Select((files, _) => files.Any(f =>
                f.Equals(MarkerFileName, StringComparison.OrdinalIgnoreCase)));

        context.RegisterSourceOutput(hasMarker, Generate);
    }

    private static void Generate(SourceProductionContext context, bool shouldGenerate)
    {
        if (!shouldGenerate)
            return;

        var json = ReadEmbeddedResource();
        if (string.IsNullOrEmpty(json))
            return;

        var countries = CountryParser.Parse(json!);
        if (countries.IsDefaultOrEmpty)
            return;

        var model = new CountryCodeGenerationModel(countries);
        var template = new CountryCodeTemplate(model);
        var artifact = template.RenderOutput();

        context.AddSource(artifact);
    }

    private static string? ReadEmbeddedResource()
    {
        var assembly = typeof(CountryCodeGenerator).Assembly;
        using var stream = assembly.GetManifestResourceStream("countries.json");

        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

// =============================================================================
// Pragmatic.Internationalization - LanguageCodeGenerator
// Source generator for ISO 639-1 language code static properties
// =============================================================================

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

using Pragmatic.Internationalization.SourceGenerator.Models;
using Pragmatic.Internationalization.SourceGenerator.Templates;

namespace Pragmatic.Internationalization.SourceGenerator;

/// <summary>
///     Generates static LanguageCode properties from embedded JSON data.
/// </summary>
[Generator]
public sealed class LanguageCodeGenerator : IIncrementalGenerator
{
    private const string MarkerFileName = ".generate-languages";

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

        var languages = LanguageParser.Parse(json!);
        if (languages.IsDefaultOrEmpty)
            return;

        var model = new LanguageCodeGenerationModel(languages);
        var template = new LanguageCodeTemplate(model);
        var artifact = template.RenderOutput();

        context.AddSource(artifact);
    }

    private static string? ReadEmbeddedResource()
    {
        var assembly = typeof(LanguageCodeGenerator).Assembly;
        using var stream = assembly.GetManifestResourceStream("languages.json");

        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

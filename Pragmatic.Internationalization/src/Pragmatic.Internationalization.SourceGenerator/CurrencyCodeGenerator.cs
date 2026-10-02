// =============================================================================
// Pragmatic.Internationalization - CurrencyCodeGenerator
// Source generator for ISO 4217 currency codes
// =============================================================================

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.Internationalization.SourceGenerator.Models;
using Pragmatic.Internationalization.SourceGenerator.Templates;

namespace Pragmatic.Internationalization.SourceGenerator;

/// <summary>
///     Source generator that generates CurrencyCode static properties from embedded currencies.json.
/// </summary>
/// <remarks>
///     <para>
///         This generator only runs when the project includes a marker file named ".generate-currencies"
///         as an AdditionalFile. This prevents duplicate generation in consumer projects.
///     </para>
///     <para>
///         The Pragmatic.Internationalization project includes this marker file.
///     </para>
/// </remarks>
[Generator]
public sealed class CurrencyCodeGenerator : IIncrementalGenerator
{
    private const string MarkerFileName = ".generate-currencies";

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

        var currencies = CurrencyParser.Parse(json!);
        if (currencies.IsDefaultOrEmpty)
            return;

        var model = new CurrencyCodeGenerationModel(currencies);
        var template = new CurrencyCodeTemplate(model);
        var artifact = template.RenderOutput();

        context.AddSource(artifact);
    }

    private static string? ReadEmbeddedResource()
    {
        var assembly = typeof(CurrencyCodeGenerator).Assembly;
        using var stream = assembly.GetManifestResourceStream("currencies.json");

        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
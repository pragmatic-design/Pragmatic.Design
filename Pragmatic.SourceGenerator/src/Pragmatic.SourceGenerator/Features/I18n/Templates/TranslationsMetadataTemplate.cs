// =============================================================================
// Pragmatic.Internationalization - TranslationsMetadataTemplate
// Generates [assembly: PragmaticMetadata] for cross-assembly discovery
// =============================================================================

using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n.Templates;

/// <summary>
///     Generates [assembly: PragmaticMetadata(MetadataCategory.Translations, ...)] attribute.
/// </summary>
internal sealed class TranslationsMetadataTemplate : CSharpTemplate
{
    private const string SchemaVersion = "1.0.0";
    private const int MetadataCategoryTranslations = 9;
    private readonly bool _indent;

    private readonly TranslationsMetadataModel _model;

    public TranslationsMetadataTemplate(TranslationsMetadataModel model, bool indent)
    {
        _model = model;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/I18n";
    protected override string? SourceInfo => $"{_model.ClassName} from {_model.Namespace}";
    protected override string? TriggerInfo => "[TranslationKeys] on assembly";

    public override Artifact RenderOutput() => new(
        "_Metadata.Translations.g.cs",
        ToSourceText());

    protected override bool Validate()
    {
        return !string.IsNullOrEmpty(_model.ClassName) &&
               _model.TotalKeys > 0;
    }

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        // Emit the assembly attribute with raw string literal
        AppendLine($"[assembly: PragmaticMetadata(MetadataCategory.Translations, \"{SchemaVersion}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();

        builder.Property("generator", "Pragmatic.Internationalization.SourceGenerator");
        builder.Property("className", _model.ClassName);

        if (!string.IsNullOrEmpty(_model.Namespace))
            builder.Property("namespace", _model.Namespace);

        builder.Property("totalKeys", _model.TotalKeys);

        if (!_model.Cultures.IsDefaultOrEmpty)
            builder.PropertyArray("cultures", _model.Cultures);

        if (!string.IsNullOrEmpty(_model.DefaultCulture))
            builder.Property("defaultCulture", _model.DefaultCulture);

        if (!_model.Files.IsDefaultOrEmpty)
            builder.PropertyArray("files", _model.Files);

        if (!string.IsNullOrEmpty(_model.ProviderType))
            builder.Property("provider", _model.ProviderType);

        builder.EndObject();

        return builder.ToString();
    }
}

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.I18n.Models;

/// <summary>
///     Represents a single translation key.
/// </summary>
/// <remarks>
///     Record (value-equatable) on purpose: these models flow through the incremental pipeline,
///     and reference equality would re-parse every translation file on each keystroke.
/// </remarks>
internal sealed record TranslationKeyModel
{
    public TranslationKeyModel(
        string fullKey,
        string propertyName,
        EquatableArray<string> pathSegments,
        string? sampleValue = null,
        EquatableDictionary<string, string>? translations = null)
    {
        FullKey = fullKey;
        PropertyName = propertyName;
        PathSegments = pathSegments;
        SampleValue = sampleValue;
        Translations = translations ?? EquatableDictionary<string, string>.Empty;
    }

    /// <summary>
    ///     The full key path (e.g., "errors.notFound").
    /// </summary>
    public string FullKey { get; }

    /// <summary>
    ///     The property name in generated code (e.g., "NotFound").
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    ///     The path segments for nesting (e.g., ["errors"] for "errors.notFound").
    /// </summary>
    public EquatableArray<string> PathSegments { get; }

    /// <summary>
    ///     Sample value from the default locale (for XML doc).
    /// </summary>
    public string? SampleValue { get; }

    /// <summary>
    ///     All translations for this key, keyed by culture code.
    ///     Example: { "en": "Hello", "it": "Ciao" }
    /// </summary>
    public EquatableDictionary<string, string> Translations { get; }

    /// <summary>
    ///     Whether this key has embedded translations (LocalizedString) or is key-only.
    /// </summary>
    public bool HasEmbeddedTranslations => !Translations.IsEmpty;

    /// <summary>
    ///     Creates a copy with additional translation.
    /// </summary>
    public TranslationKeyModel WithTranslation(string culture, string value)
    {
        return new TranslationKeyModel(
            FullKey,
            PropertyName,
            PathSegments,
            SampleValue ?? value,
            Translations.Add(culture, value));
    }
}

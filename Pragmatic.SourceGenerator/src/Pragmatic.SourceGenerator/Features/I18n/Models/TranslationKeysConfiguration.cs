// =============================================================================
// Pragmatic.Internationalization - TranslationKeysConfiguration
// Configuration model parsed from [TranslationKeys] attribute
// =============================================================================

namespace Pragmatic.SourceGenerator.Features.I18n.Models;

/// <summary>
///     Configuration for translation keys generation.
///     Parsed from [TranslationKeys] assembly attribute.
/// </summary>
internal sealed record TranslationKeysConfiguration
{
    /// <summary>
    ///     The root class name (default: "T").
    /// </summary>
    public string ClassName { get; init; } = "T";

    /// <summary>
    ///     Custom namespace for generated code (empty = derive from path).
    /// </summary>
    public string Namespace { get; init; } = "";

    /// <summary>
    ///     Whether to embed translations at compile-time.
    ///     true = LocalizedString.From(...), false = LocalizationKey.
    /// </summary>
    public bool EmbedTranslations { get; init; } = true;

    /// <summary>
    ///     Whether to organize by file (nested classes per file).
    ///     true = T.Common.Key, T.Errors.Key
    ///     false = T.Key (flat)
    /// </summary>
    public bool ByFile { get; init; } = true;

    /// <summary>
    ///     The default culture code for ordering.
    /// </summary>
    public string DefaultCulture { get; init; } = "en";

    /// <summary>
    ///     Default configuration instance.
    /// </summary>
    public static TranslationKeysConfiguration Default { get; } = new();
}
